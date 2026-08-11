using Hercules.Mesh;
using Xunit;

namespace Hercules.Agent.Tests.Phase4Tests;

public class CircuitBreakerTests
{
   [Fact]
   public void New_Peer_Has_Closed_Circuit()
   {
      var cb = new CircuitBreaker();
      Assert.Equal(CircuitState.Closed, cb.GetState("new-peer"));
      Assert.True(cb.CanSend("new-peer"));
   }

   [Fact]
   public void Failures_Below_Threshold_Keep_Circuit_Closed()
   {
      var cb = new CircuitBreaker { FailureThreshold = 5 };
      for (var i = 0; i < 4; i++) cb.RecordFailure("peer-a");
      Assert.Equal(CircuitState.Closed, cb.GetState("peer-a"));
      Assert.True(cb.CanSend("peer-a"));
   }

   [Fact]
   public void Failures_At_Threshold_Open_Circuit()
   {
      var cb = new CircuitBreaker { FailureThreshold = 3 };
      for (var i = 0; i < 3; i++) cb.RecordFailure("peer-b");
      Assert.Equal(CircuitState.Open, cb.GetState("peer-b"));
      Assert.False(cb.CanSend("peer-b"));
   }

   [Fact]
   public void Success_Resets_Circuit_To_Closed()
   {
      var cb = new CircuitBreaker { FailureThreshold = 2 };
      cb.RecordFailure("peer-c");
      cb.RecordFailure("peer-c");
      Assert.Equal(CircuitState.Open, cb.GetState("peer-c"));

      cb.RecordSuccess("peer-c");
      Assert.Equal(CircuitState.Closed, cb.GetState("peer-c"));
      Assert.True(cb.CanSend("peer-c"));
   }

   [Fact]
   public void Open_Circuit_Transitions_To_HalfOpen_After_Cooldown()
   {
      var cb = new CircuitBreaker { FailureThreshold = 1, Cooldown = TimeSpan.FromMilliseconds(50) };
      cb.RecordFailure("peer-d");
      Assert.Equal(CircuitState.Open, cb.GetState("peer-d"));
      Assert.False(cb.CanSend("peer-d"));

      Thread.Sleep(60);
      // После cooldown — CanSend возвращает true и переводит в HalfOpen
      Assert.True(cb.CanSend("peer-d"));
      Assert.Equal(CircuitState.HalfOpen, cb.GetState("peer-d"));
   }

   [Fact]
   public void HalfOpen_Success_Closes_Circuit()
   {
      var cb = new CircuitBreaker { FailureThreshold = 1, Cooldown = TimeSpan.FromMilliseconds(10) };
      cb.RecordFailure("peer-e");
      Thread.Sleep(15);
      cb.CanSend("peer-e"); // Переход в HalfOpen
      Assert.Equal(CircuitState.HalfOpen, cb.GetState("peer-e"));

      cb.RecordSuccess("peer-e");
      Assert.Equal(CircuitState.Closed, cb.GetState("peer-e"));
   }

   [Fact]
   public void HalfOpen_Failure_Reopens_Circuit()
   {
      var cb = new CircuitBreaker { FailureThreshold = 1, Cooldown = TimeSpan.FromMilliseconds(10) };
      cb.RecordFailure("peer-f");
      Thread.Sleep(15);
      cb.CanSend("peer-f"); // HalfOpen
      cb.RecordFailure("peer-f"); // Снова fail

      Assert.Equal(CircuitState.Open, cb.GetState("peer-f"));
   }

   [Fact]
   public void Reset_Force_Closes_Circuit()
   {
      var cb = new CircuitBreaker { FailureThreshold = 1 };
      cb.RecordFailure("peer-g");
      Assert.Equal(CircuitState.Open, cb.GetState("peer-g"));

      cb.Reset("peer-g");
      Assert.Equal(CircuitState.Closed, cb.GetState("peer-g"));
   }

   [Fact]
   public void GetAllStates_Returns_All_Tracked_Peers()
   {
      var cb = new CircuitBreaker { FailureThreshold = 1 };
      cb.RecordFailure("peer-1");
      cb.RecordFailure("peer-2");
      // peer-3 — сначала failure (создаёт запись), потом success (сбрасывает)
      cb.RecordFailure("peer-3");
      cb.RecordSuccess("peer-3");

      var states = cb.GetAllStates();
      Assert.Equal(3, states.Count);
      Assert.Equal(CircuitState.Open, states["peer-1"]);
      Assert.Equal(CircuitState.Open, states["peer-2"]);
      Assert.Equal(CircuitState.Closed, states["peer-3"]);
   }
}

public class RetryPolicyTests
{
   [Fact]
   public void GetDelay_Returns_Zero_For_First_Attempt()
   {
      var policy = new RetryPolicy();
      Assert.Equal(TimeSpan.Zero, policy.GetDelay(0));
   }

   [Fact]
   public void GetDelay_Increases_Exponentially()
   {
      var policy = new RetryPolicy
      {
         BaseDelay = TimeSpan.FromMilliseconds(100),
         BackoffMultiplier = 2.0
      };
      var d1 = policy.GetDelay(1).TotalMilliseconds;
      var d2 = policy.GetDelay(2).TotalMilliseconds;
      var d3 = policy.GetDelay(3).TotalMilliseconds;

      Assert.Equal(100, d1, 1);
      Assert.Equal(200, d2, 1);
      Assert.Equal(400, d3, 1);
   }

   [Fact]
   public void GetDelay_Clamped_To_MaxDelay()
   {
      var policy = new RetryPolicy
      {
         BaseDelay = TimeSpan.FromSeconds(1),
         MaxDelay = TimeSpan.FromSeconds(2)
      };
      var d10 = policy.GetDelay(10);
      Assert.True(d10 <= TimeSpan.FromSeconds(2));
   }

   [Fact]
   public void ShouldRetry_True_For_Timeout()
   {
      var policy = new RetryPolicy { MaxAttempts = 3 };
      var resp = IntentResponse.TimedOut("req", "agent");
      Assert.True(policy.ShouldRetry(resp, 0));
   }

   [Fact]
   public void ShouldRetry_True_For_Error()
   {
      var policy = new RetryPolicy { MaxAttempts = 3 };
      var resp = IntentResponse.Failed("req", "agent", "err");
      Assert.True(policy.ShouldRetry(resp, 0));
   }

   [Fact]
   public void ShouldRetry_False_For_Success()
   {
      var policy = new RetryPolicy { MaxAttempts = 3 };
      var resp = IntentResponse.Ok("req", "agent", "result");
      Assert.False(policy.ShouldRetry(resp, 0));
   }

   [Fact]
   public void ShouldRetry_False_At_MaxAttempts()
   {
      var policy = new RetryPolicy { MaxAttempts = 2 };
      var resp = IntentResponse.TimedOut("req", "agent");
      // attempt 0 < MaxAttempts (2) — можно ретраить
      Assert.True(policy.ShouldRetry(resp, 0));
      // attempt 1 = MaxAttempts-1 — последний, ретраить нельзя
      Assert.False(policy.ShouldRetry(resp, 1));
   }
}