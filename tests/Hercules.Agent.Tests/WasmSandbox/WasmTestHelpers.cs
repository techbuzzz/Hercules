using System.Text;

namespace Hercules.Agent.Tests.WasmSandbox;

/// <summary>
///     Хелперы для генерации минимальных WASM-модулей без внешних зависимостей (wabt/wat2wasm).
///     Все модули совместимы с Wasmtime.NET v14 (WASI preview1, "_start" entry point).
/// </summary>
internal static class WasmTestHelpers
{
    /// <summary>
    ///     Минимальный WASM с функцией _start, которая ничего не делает.
    /// </summary>
    public static byte[] BuildEmptyModule()
    {
        var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(new byte[] { 0x00, 0x61, 0x73, 0x6D });
        bw.Write(new byte[] { 0x01, 0x00, 0x00, 0x00 });
        WriteSection(bw, 1, w => { w.Write((byte)1); w.Write((byte)0x60); w.Write((byte)0); w.Write((byte)0); });
        WriteSection(bw, 3, w => { w.Write((byte)1); w.Write((byte)0); });
        WriteSection(bw, 5, w => { w.Write((byte)1); w.Write((byte)0); w.Write((byte)1); });
        WriteSection(bw, 7, w => { w.Write((byte)1); WriteName(w, "_start"); w.Write((byte)0); w.Write((byte)0); });
        WriteSection(bw, 10, w => { w.Write((byte)1); w.Write((byte)2); w.Write((byte)0); w.Write((byte)0x0B); });
        return ms.ToArray();
    }

    /// <summary>
    ///     WASM с бесконечным циклом (loop w/ br 0). Используется для теста fuel exhaustion.
    /// </summary>
    public static byte[] BuildInfiniteLoopModule()
    {
        var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(new byte[] { 0x00, 0x61, 0x73, 0x6D });
        bw.Write(new byte[] { 0x01, 0x00, 0x00, 0x00 });
        WriteSection(bw, 1, w => { w.Write((byte)1); w.Write((byte)0x60); w.Write((byte)0); w.Write((byte)0); });
        WriteSection(bw, 3, w => { w.Write((byte)1); w.Write((byte)0); });
        WriteSection(bw, 5, w => { w.Write((byte)1); w.Write((byte)0); w.Write((byte)1); });
        WriteSection(bw, 7, w => { w.Write((byte)1); WriteName(w, "_start"); w.Write((byte)0); w.Write((byte)0); });
        var body = new byte[] { 0x00, 0x03, 0x40, 0x0C, 0x00, 0x0B, 0x0B };
        WriteSection(bw, 10, w =>
        {
            w.Write((byte)1);
            WriteU32LEB128(w, (uint)body.Length);
            w.Write(body);
        });
        return ms.ToArray();
    }

    /// <summary>
    ///     WASM, который запрашивает огромный memory.grow (≈2G страниц) в цикле.
    ///     memory.grow возвращает -1 (fail), но цикл бесконечный, fuel должен убить выполнение.
    ///     Используется для теста fuel exhaustion через аллокацию.
    /// </summary>
    public static byte[] BuildMemoryGrowthLargeModule()
    {
        var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(new byte[] { 0x00, 0x61, 0x73, 0x6D });
        bw.Write(new byte[] { 0x01, 0x00, 0x00, 0x00 });
        WriteSection(bw, 1, w => { w.Write((byte)1); w.Write((byte)0x60); w.Write((byte)0); w.Write((byte)0); });
        WriteSection(bw, 3, w => { w.Write((byte)1); w.Write((byte)0); });
        WriteSection(bw, 5, w => { w.Write((byte)1); w.Write((byte)0); w.Write((byte)1); });
        WriteSection(bw, 7, w => { w.Write((byte)1); WriteName(w, "_start"); w.Write((byte)0); w.Write((byte)0); });

        // loop { i32.const 0x7FFFFFFF (max i32 → fail); memory.grow 0; drop; br 0 }; end
        var body = new byte[]
        {
            0x00,
            0x03, 0x40,
            0x41, 0xFF, 0xFF, 0xFF, 0xFF, 0x07,
            0x40, 0x00,
            0x1A,
            0x0C, 0x00,
            0x0B,
            0x0B
        };
        WriteSection(bw, 10, w =>
        {
            w.Write((byte)1);
            WriteU32LEB128(w, (uint)body.Length);
            w.Write(body);
        });
        return ms.ToArray();
    }

    /// <summary>
    ///     WASM, который импортирует сокетную функцию — НЕ зарегистрирована в нашем Linker.
    ///     Wasmtime должен вернуть failed со trap "unknown import".
    /// </summary>
    public static byte[] BuildModuleWithSocketImport()
    {
        var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(new byte[] { 0x00, 0x61, 0x73, 0x6D });
        bw.Write(new byte[] { 0x01, 0x00, 0x00, 0x00 });
        WriteSection(bw, 1, w => { w.Write((byte)1); w.Write((byte)0x60); w.Write((byte)0); w.Write((byte)0); });
        WriteSection(bw, 2, w =>
        {
            w.Write((byte)1);
            WriteName(w, "wasi_snapshot_preview1");
            WriteName(w, "sock_connect");
            w.Write((byte)0); w.Write((byte)0);
        });
        WriteSection(bw, 3, w => { w.Write((byte)1); w.Write((byte)0); });
        WriteSection(bw, 5, w => { w.Write((byte)1); w.Write((byte)0); w.Write((byte)1); });
        WriteSection(bw, 7, w => { w.Write((byte)1); WriteName(w, "_start"); w.Write((byte)0); w.Write((byte)0); });
        var body = new byte[] { 0x00, 0x10, 0x00, 0x0B };
        WriteSection(bw, 10, w =>
        {
            w.Write((byte)1);
            WriteU32LEB128(w, (uint)body.Length);
            w.Write(body);
        });
        return ms.ToArray();
    }

    private static void WriteSection(BinaryWriter bw, byte id, Action<BinaryWriter> content)
    {
        var sectionMs = new MemoryStream();
        using var sectionBw = new BinaryWriter(sectionMs);
        content(sectionBw);
        sectionBw.Flush();
        var payload = sectionMs.ToArray();
        bw.Write(id);
        WriteU32LEB128(bw, (uint)payload.Length);
        bw.Write(payload);
    }

    private static void WriteName(BinaryWriter w, string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        WriteU32LEB128(w, (uint)bytes.Length);
        w.Write(bytes);
    }

    private static void WriteU32LEB128(BinaryWriter w, uint value)
    {
        while (true)
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value == 0) { w.Write(b); return; }
            w.Write((byte)(b | 0x80));
        }
    }
}
