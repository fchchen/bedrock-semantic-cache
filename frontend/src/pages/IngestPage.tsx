import { useState, useRef, useCallback, useEffect } from "react";
import { ingestDocument, reingestDocument, getJobStatus, type IngestJob } from "../api";

export default function IngestPage() {
  const [documentId, setDocumentId] = useState("");
  const [fileName, setFileName] = useState("");
  const [content, setContent] = useState("");
  const [reingest, setReingest] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [job, setJob] = useState<IngestJob | null>(null);
  const pollingRef = useRef<ReturnType<typeof setInterval> | undefined>(undefined);

  const stopPolling = useCallback(() => {
    if (pollingRef.current) {
      clearInterval(pollingRef.current);
      pollingRef.current = undefined;
    }
  }, []);

  useEffect(() => stopPolling, [stopPolling]);

  function startPolling(jobId: string) {
    stopPolling();
    pollingRef.current = setInterval(async () => {
      try {
        const updated = await getJobStatus(jobId);
        setJob(updated);
        if (updated.status !== "Processing") {
          stopPolling();
        }
      } catch {
        stopPolling();
      }
    }, 1000);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!documentId.trim() || !fileName.trim() || !content.trim()) return;

    setLoading(true);
    setError(null);
    setJob(null);
    stopPolling();

    try {
      const result = reingest
        ? await reingestDocument(documentId.trim(), fileName.trim(), content)
        : await ingestDocument(documentId.trim(), fileName.trim(), content);
      setJob(result);
      startPolling(result.id);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Something went wrong");
    } finally {
      setLoading(false);
    }
  }

  function handleFile(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    if (!fileName) setFileName(file.name);
    const reader = new FileReader();
    reader.onload = () => setContent(reader.result as string);
    reader.readAsText(file);
  }

  return (
    <div className="ingest-container">
      <h2>Ingest Document</h2>
      <form onSubmit={handleSubmit}>
        <div className="form-group">
          <label>Document ID</label>
          <input value={documentId} onChange={(e) => setDocumentId(e.target.value)} placeholder="e.g. doc-001" />
        </div>
        <div className="form-group">
          <label>File Name</label>
          <input value={fileName} onChange={(e) => setFileName(e.target.value)} placeholder="e.g. readme.md" />
        </div>
        <div className="form-group">
          <label>Content</label>
          <textarea value={content} onChange={(e) => setContent(e.target.value)} placeholder="Paste document text or use the file picker below..." />
        </div>
        <label className="file-label">
          Choose file...
          <input type="file" accept=".txt,.md,.csv,.json,.xml,.html" onChange={handleFile} />
        </label>
        <div className="reingest-check">
          <input type="checkbox" id="reingest" checked={reingest} onChange={(e) => setReingest(e.target.checked)} />
          <label htmlFor="reingest">Re-ingest (replace existing document & invalidate cache)</label>
        </div>
        <div className="form-actions">
          <button type="submit" className="btn" disabled={loading || !documentId.trim() || !fileName.trim() || !content.trim()}>
            {loading ? "Submitting..." : reingest ? "Re-ingest" : "Ingest"}
          </button>
        </div>
      </form>

      {error && <div className="error-text">{error}</div>}

      {job && (
        <div className="job-status">
          <div className="status-line">
            <span>Job ID</span>
            <span>{job.id}</span>
          </div>
          <div className="status-line">
            <span>Status</span>
            <span className={`status-value ${job.status.toLowerCase()}`}>{job.status}</span>
          </div>
          <div className="status-line">
            <span>Document</span>
            <span>{job.documentId}</span>
          </div>
          {job.error && <div className="error-text">{job.error}</div>}
        </div>
      )}
    </div>
  );
}
