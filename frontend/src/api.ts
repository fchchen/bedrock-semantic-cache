export interface ChatResponse {
  answer: string;
  cacheStatus: string;
  sources: string[];
  latencyMs?: number;
}

export interface IngestJob {
  id: string;
  status: string;
  documentId: string;
  fileName: string;
  error?: string;
}

export async function sendChat(prompt: string): Promise<{ data: ChatResponse; cacheStatus: string; latencyMs: number }> {
  const start = performance.now();
  const res = await fetch("/chat", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ prompt }),
  });
  const latencyMs = Math.round(performance.now() - start);
  if (!res.ok) {
    const problem = await res.json().catch(() => null);
    throw new Error(problem?.detail ?? `Request failed (${res.status})`);
  }
  const data: ChatResponse = await res.json();
  const cacheStatus = res.headers.get("X-Cache-Status") ?? data.cacheStatus ?? "UNKNOWN";
  return { data, cacheStatus, latencyMs };
}

export async function ingestDocument(documentId: string, fileName: string, content: string): Promise<IngestJob> {
  const res = await fetch("/ingest", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ documentId, fileName, content }),
  });
  if (!res.ok) {
    const problem = await res.json().catch(() => null);
    throw new Error(problem?.detail ?? `Request failed (${res.status})`);
  }
  return res.json();
}

export async function getJobStatus(jobId: string): Promise<IngestJob> {
  const res = await fetch(`/ingest/${jobId}`);
  if (!res.ok) {
    throw new Error(`Job not found (${res.status})`);
  }
  return res.json();
}

export async function reingestDocument(documentId: string, fileName: string, content: string): Promise<IngestJob> {
  const res = await fetch(`/ingest/${encodeURIComponent(documentId)}/reingest`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ documentId, fileName, content }),
  });
  if (!res.ok) {
    const problem = await res.json().catch(() => null);
    throw new Error(problem?.detail ?? `Request failed (${res.status})`);
  }
  return res.json();
}
