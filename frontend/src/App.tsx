import { NavLink, Routes, Route, Navigate } from "react-router-dom";
import ChatPage from "./pages/ChatPage";
import IngestPage from "./pages/IngestPage";

export default function App() {
  return (
    <div className="app">
      <nav className="nav">
        <span className="nav-brand">Bedrock Semantic Cache</span>
        <div className="nav-links">
          <NavLink to="/chat" className={({ isActive }) => (isActive ? "nav-link active" : "nav-link")}>
            Chat
          </NavLink>
          <NavLink to="/ingest" className={({ isActive }) => (isActive ? "nav-link active" : "nav-link")}>
            Ingest
          </NavLink>
        </div>
      </nav>
      <main className="main">
        <Routes>
          <Route path="/chat" element={<ChatPage />} />
          <Route path="/ingest" element={<IngestPage />} />
          <Route path="*" element={<Navigate to="/chat" replace />} />
        </Routes>
      </main>
    </div>
  );
}
