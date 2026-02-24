import { useState, useRef, useEffect, type KeyboardEvent } from "react";
import { sendChat } from "../api";

interface Message {
  role: "user" | "assistant";
  text: string;
  cacheStatus?: string;
  latencyMs?: number;
}

export default function ChatPage() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  async function handleSend() {
    const prompt = input.trim();
    if (!prompt || loading) return;

    setInput("");
    setError(null);
    setMessages((prev) => [...prev, { role: "user", text: prompt }]);
    setLoading(true);

    try {
      const { data, cacheStatus, latencyMs } = await sendChat(prompt);
      setMessages((prev) => [
        ...prev,
        { role: "assistant", text: data.answer, cacheStatus, latencyMs },
      ]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Something went wrong");
    } finally {
      setLoading(false);
    }
  }

  function handleKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  }

  return (
    <div className="chat-container">
      <div className="chat-messages">
        {messages.length === 0 && !loading && (
          <div className="empty-state">Send a message to start chatting</div>
        )}
        {messages.map((msg, i) => (
          <div key={i} className={`message ${msg.role}`}>
            <div>{msg.text}</div>
            {msg.role === "assistant" && (
              <div className="message-meta">
                {msg.cacheStatus && (
                  <span className={`badge ${msg.cacheStatus === "HIT" ? "hit" : "miss"}`}>
                    {msg.cacheStatus}
                  </span>
                )}
                {msg.latencyMs != null && (
                  <span className="badge latency">{msg.latencyMs}ms</span>
                )}
              </div>
            )}
          </div>
        ))}
        {loading && (
          <div className="message assistant">
            <div style={{ opacity: 0.5 }}>Thinking...</div>
          </div>
        )}
        {error && <div className="error-text">{error}</div>}
        <div ref={bottomRef} />
      </div>
      <div className="chat-input-bar">
        <textarea
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Ask a question... (Enter to send, Shift+Enter for newline)"
          rows={1}
          disabled={loading}
        />
        <button className="btn" onClick={handleSend} disabled={loading || !input.trim()}>
          Send
        </button>
      </div>
    </div>
  );
}
