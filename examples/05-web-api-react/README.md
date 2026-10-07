# 05 — Web API + React frontend

Embed Hercules as a backend and build a small React UI on top.

## What you need

- Node.js 22+
- .NET 10 SDK

## Run

1. Start the Web API:

   ```bash
   dotnet run --project src/agent/Hercules.WebApi
   ```

2. In another terminal, start the React app:

   ```bash
   cd examples/05-web-api-react/react-ui
   npm install
   npm run dev
   ```

3. Open `http://localhost:5173` and chat with Hercules.

## What happens

- The React UI calls `POST /api/chat` on `http://localhost:8421`.
- Responses are streamed and rendered as Markdown.
- Skills and memory can be viewed on separate tabs.

## Files

- `react-ui/` — minimal Vite + React + Tailwind app
- `README.md` — this file
