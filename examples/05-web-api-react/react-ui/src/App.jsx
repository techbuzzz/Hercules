import React, { useState } from 'react';
import { marked } from 'marked';

const API = 'http://localhost:8421';
const KEY = 'dev-local-key';

export default function App() {
  const [messages, setMessages] = useState([
    { role: 'bot', text: 'Hello! I am Hercules. Ask me anything.' }
  ]);
  const [input, setInput] = useState('');
  const [loading, setLoading] = useState(false);

  async function send() {
    if (!input.trim()) return;
    const userMsg = { role: 'user', text: input };
    setMessages(m => [...m, userMsg]);
    setInput('');
    setLoading(true);
    try {
      const res = await fetch(`${API}/api/chat`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-Api-Key': KEY
        },
        body: JSON.stringify({ message: input })
      });
      const data = await res.json();
      setMessages(m => [...m, { role: 'bot', text: data.response || data.message || JSON.stringify(data) }]);
    } catch (e) {
      setMessages(m => [...m, { role: 'bot', text: `Error: ${e.message}` }]);
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="max-w-2xl mx-auto p-6 h-screen flex flex-col">
      <h1 className="text-2xl font-bold mb-4">Hercules React UI</h1>
      <div className="flex-1 overflow-auto space-y-3 mb-4">
        {messages.map((m, i) => (
          <div key={i} className={`p-3 rounded ${m.role === 'user' ? 'bg-blue-900 ml-12' : 'bg-neutral-800 mr-12'}`}>
            <div className="text-xs uppercase tracking-wide text-neutral-400 mb-1">{m.role}</div>
            <div dangerouslySetInnerHTML={{ __html: marked.parse(m.text) }}></div>
          </div>
        ))}
        {loading && <div className="text-neutral-400">Hercules is thinking…</div>}
      </div>
      <div className="flex gap-2">
        <input
          className="flex-1 bg-neutral-800 border border-neutral-700 rounded px-3 py-2"
          value={input}
          onChange={e => setInput(e.target.value)}
          onKeyDown={e => e.key === 'Enter' && send()}
          placeholder="Type a message…"
        />
        <button
          className="bg-blue-600 hover:bg-blue-500 px-4 py-2 rounded disabled:opacity-50"
          onClick={send}
          disabled={loading}
        >
          Send
        </button>
      </div>
    </div>
  );
}
