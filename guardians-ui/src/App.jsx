import { useState, useEffect, useCallback, useRef } from "react";
import { api } from "./api.js";

/* ─── Global styles injected once ──────────────────────────────────────── */
const GLOBAL_CSS = `
  *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
  :root {
    --bg:      #0a0e1a;
    --bg2:     #111827;
    --bg3:     #1a2235;
    --border:  rgba(255,255,255,0.08);
    --border2: rgba(255,255,255,0.14);
    --txt:     #f0f4ff;
    --txt2:    #8b9bbf;
    --txt3:    #5a6a8a;
    --blue:    #3b82f6;
    --blueD:   #1d4ed8;
    --blueL:   rgba(59,130,246,0.15);
    --green:   #22c55e;
    --greenL:  rgba(34,197,94,0.15);
    --red:     #ef4444;
    --redL:    rgba(239,68,68,0.15);
    --amber:   #f59e0b;
    --amberL:  rgba(245,158,11,0.15);
    --purple:  #8b5cf6;
    --purpleL: rgba(139,92,246,0.18);
    --chain:   #7c3aed;
    --chainL:  rgba(124,58,237,0.18);
    --chainG:  #a78bfa;
    --cyan:    #06b6d4;
    --cyanL:   rgba(6,182,212,0.15);
    --r:       10px;
    --r2:      14px;
  }
  html, body, #root { height: 100%; }
  body { background: var(--bg); color: var(--txt); font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; line-height: 1.6; -webkit-font-smoothing: antialiased; overflow-x: hidden; }
  /* --- Responsive layout (mobile / tablet) --- */
  .gl-dashboard-stats {
    display: grid;
    grid-template-columns: repeat(4, minmax(0, 1fr));
    gap: 14px;
    margin-bottom: 24px;
  }
  .gl-dashboard-split {
    display: grid;
    grid-template-columns: minmax(0, 280px) minmax(0, 1fr);
    gap: 16px;
  }
  .gl-blockchain-stats {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 14px;
    margin-bottom: 24px;
  }
  .gl-blockchain-split {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 16px;
  }
  .gl-alerts-stats {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 14px;
    margin-bottom: 24px;
  }
  .gl-table-scroll {
    overflow-x: auto;
    -webkit-overflow-scrolling: touch;
    max-width: 100%;
  }
  .gl-table-scroll table { min-width: 520px; }
  .gl-table-scroll--wide table { min-width: 780px; }
  .gl-stat-value { font-size: 32px; font-weight: 700; color: var(--txt); line-height: 1; }
  @media (max-width: 900px) {
    .gl-dashboard-stats { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .gl-dashboard-split { grid-template-columns: 1fr; }
    .gl-blockchain-stats { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .gl-alerts-stats { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .gl-stat-value { font-size: 26px; }
  }
  @media (max-width: 560px) {
    .gl-dashboard-stats,
    .gl-blockchain-stats,
    .gl-alerts-stats { grid-template-columns: 1fr; }
    .gl-blockchain-split { grid-template-columns: 1fr; }
    .gl-stat-value { font-size: 22px; }
    .gl-dashboard-stats > div,
    .gl-blockchain-stats > div,
    .gl-alerts-stats > div { padding: 14px 14px !important; }
  }
  .gl-header {
    display: flex;
    align-items: center;
    padding: 0 24px;
    gap: 16px;
    min-height: 54px;
    flex-wrap: nowrap;
  }
  .gl-nav { display: flex; gap: 2px; flex: 1; overflow-x: auto; min-width: 0; -webkit-overflow-scrolling: touch; }
  .gl-main { flex: 1; max-width: 1180px; margin: 0 auto; padding: 28px 24px 60px; width: 100%; }
  .gl-page-title { font-size: 20px; font-weight: 700; margin-bottom: 22px; display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
  .gl-modal-actions { display: flex; gap: 10px; flex-wrap: wrap; }
  @media (max-width: 560px) {
    .gl-header { padding: 8px 12px; flex-wrap: wrap; row-gap: 8px; height: auto !important; }
    .gl-nav { order: 3; width: 100%; flex: none; padding-bottom: 2px; }
    .gl-nav button { padding: 5px 9px !important; font-size: 11px !important; }
    .gl-main { padding: 16px 12px 48px; }
    .gl-page-title { font-size: 17px; margin-bottom: 14px; }
    .gl-modal-actions { flex-direction: column; }
    .gl-modal-actions > button { width: 100%; }
  }
  .gl-form-two-col {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 12px;
    margin-bottom: 16px;
  }
  @media (max-width: 560px) {
    .gl-form-two-col { grid-template-columns: 1fr; }
  }
  .gl-action-row { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }
  .gl-offline-banner { padding: 10px 24px; font-size: 13px; }
  @media (max-width: 560px) {
    .gl-offline-banner { padding: 10px 12px; font-size: 12px; }
  }
  .gl-kv-row { display: flex; gap: 8px; margin-bottom: 6px; font-size: 12px; flex-wrap: wrap; align-items: flex-start; }
  .gl-kv-row > span:first-child { color: var(--txt3); min-width: 72px; flex-shrink: 0; }
  .gl-kv-row > span:last-child { flex: 1; min-width: 0; word-break: break-all; }
  .gl-alerts-card-head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px; flex-wrap: wrap; gap: 8px; }
  ::-webkit-scrollbar { width: 5px; height: 5px; }
  ::-webkit-scrollbar-track { background: transparent; }
  ::-webkit-scrollbar-thumb { background: var(--border2); border-radius: 10px; }
  a { color: var(--blue); text-decoration: none; }
  a:hover { text-decoration: underline; }
  @keyframes spin { to { transform: rotate(360deg); } }
  @keyframes pulse { 0%,100%{opacity:1} 50%{opacity:.4} }
  @keyframes fadeUp { from { opacity:0; transform:translateY(8px); } to { opacity:1; transform:none; } }
  @keyframes chainPop { 0%{transform:scale(0.9);opacity:0} 60%{transform:scale(1.04)} 100%{transform:scale(1);opacity:1} }
  @keyframes glow { 0%,100%{box-shadow:0 0 10px rgba(139,92,246,.2)} 50%{box-shadow:0 0 24px rgba(139,92,246,.5)} }
  .fade-up { animation: fadeUp 0.25s ease; }
  .chain-pop { animation: chainPop 0.4s cubic-bezier(.34,1.56,.64,1); }
  input, select, textarea {
    background: var(--bg3); color: var(--txt); border: 1px solid var(--border2);
    border-radius: var(--r); padding: 9px 12px; font-size: 13px; width: 100%;
    outline: none; transition: border-color .15s;
  }
  input:focus, select:focus, textarea:focus { border-color: var(--blue); }
  select option { background: var(--bg2); }
  button { cursor: pointer; font-family: inherit; }
`;

/* ─── Token helpers ─────────────────────────────────────────────────────── */
const fmt  = v  => `${Math.round(v * 100)}%`;
const ago  = d  => {
  if (!d) return "–";
  const s = (Date.now() - new Date(d)) / 1000;
  if (s < 60)    return `${Math.round(s)}s ago`;
  if (s < 3600)  return `${Math.round(s / 60)}m ago`;
  if (s < 86400) return `${Math.round(s / 3600)}h ago`;
  return `${Math.round(s / 86400)}d ago`;
};
const dur  = (a, b) => !b ? "–" : (() => {
  const s = Math.round((new Date(b) - new Date(a)) / 1000);
  return s < 60 ? `${s}s` : `${Math.round(s/60)}m ${s%60}s`;
})();
const short = (s, n=18) => !s ? "–" : s.length<=n ? s : `${s.slice(0,8)}…${s.slice(-6)}`;

const STATUS_C = {
  Detected:     { bg:"var(--amberL)",  txt:"var(--amber)" },
  UnderReview:  { bg:"var(--blueL)",   txt:"var(--blue)" },
  TakedownSent: { bg:"var(--purpleL)", txt:"var(--purple)" },
  Resolved:     { bg:"var(--greenL)",  txt:"var(--green)" },
  FalsePositive:{ bg:"rgba(255,255,255,0.05)", txt:"var(--txt3)" },
};
const PLAT_I  = { YouTube:"▶", Twitter:"𝕏", Instagram:"◈", Telegram:"✈", Reddit:"◉" };
const SCAN_C  = { Completed:"var(--green)", Running:"var(--blue)", Failed:"var(--red)", Queued:"var(--txt3)" };

/* ─── Primitive UI atoms ────────────────────────────────────────────────── */
const Spinner = ({ size=20 }) => (
  <div style={{ width:size, height:size, border:`2px solid var(--border2)`,
    borderTopColor:"var(--blue)", borderRadius:"50%",
    animation:"spin .7s linear infinite", flexShrink:0 }} />
);

const Loading = () => (
  <div style={{ display:"flex", alignItems:"center", justifyContent:"center",
    padding:60, gap:12, color:"var(--txt2)", fontSize:14 }}>
    <Spinner /> Loading…
  </div>
);

const Err = ({ msg }) => (
  <div style={{ background:"var(--redL)", border:"1px solid var(--red)",
    borderRadius:var_r2, padding:"14px 18px" }}>
    <div style={{ fontWeight:600, color:"var(--red)", marginBottom:4 }}>Backend not reachable</div>
    <div style={{ fontSize:13, color:"var(--txt2)" }}>{msg}</div>
    <div style={{ fontSize:12, color:"var(--txt3)", marginTop:8 }}>
      Run: <code style={{ background:"rgba(255,255,255,.07)", padding:"2px 6px", borderRadius:4 }}>
        cd GuardianLens.API → dotnet run
      </code>
    </div>
  </div>
);

const var_r2 = "var(--r2)";

const Card = ({ children, style, glow }) => (
  <div style={{ background:"var(--bg2)", border:`1px solid ${glow ? "var(--chain)" : "var(--border)"}`,
    borderRadius:var_r2, padding:"20px 22px", animation: glow ? "glow 3s ease-in-out infinite" : undefined,
    ...style }}>
    {children}
  </div>
);

const Pill = ({ label, color, bg }) => (
  <span style={{ fontSize:11, fontWeight:600, padding:"3px 9px", borderRadius:20,
    background: bg || "rgba(255,255,255,.07)", color: color || "var(--txt2)",
    display:"inline-flex", alignItems:"center", gap:5 }}>
    {label}
  </span>
);

const StatusPill = ({ status }) => {
  const c = STATUS_C[status] ?? { bg:"rgba(255,255,255,.05)", txt:"var(--txt3)" };
  return <Pill label={status} bg={c.bg} color={c.txt} />;
};

const Btn = ({ children, onClick, disabled, color="var(--blue)", variant="solid",
               small, full, loading: isLoading }) => {
  const base = {
    border:"none", borderRadius:9, fontWeight:600, cursor: disabled?"not-allowed":"pointer",
    fontSize: small ? 12 : 13, padding: small ? "5px 12px" : "9px 20px",
    width: full ? "100%" : undefined, transition:"all .15s",
    display:"inline-flex", alignItems:"center", gap:6, justifyContent:"center",
    opacity: disabled ? 0.5 : 1,
  };
  if (variant === "ghost") {
    return <button onClick={onClick} disabled={disabled} style={{ ...base,
      background:"transparent", color:"var(--txt2)",
      border:"1px solid var(--border2)" }}>
      {isLoading && <Spinner size={13} />}{children}
    </button>;
  }
  return <button onClick={onClick} disabled={disabled} style={{ ...base,
    background: disabled ? "rgba(255,255,255,.08)" : color, color:"#fff" }}>
    {isLoading && <Spinner size={13} />}{children}
  </button>;
};

const SLabel = ({ children }) => (
  <div style={{ fontSize:10, fontWeight:700, textTransform:"uppercase",
    letterSpacing:"0.09em", color:"var(--txt3)", marginBottom:7 }}>{children}</div>
);

const StatCard = ({ label, value, accent, sub, icon }) => (
  <div style={{ background:"var(--bg2)", border:`1px solid var(--border)`,
    borderTop:`2px solid ${accent}`, borderRadius:var_r2, padding:"18px 20px" }}>
    <div style={{ fontSize:11, fontWeight:600, textTransform:"uppercase",
      letterSpacing:".08em", color:"var(--txt3)", marginBottom:6, display:"flex",
      alignItems:"center", gap:6 }}>
      {icon && <span style={{ fontSize:14 }}>{icon}</span>}{label}
    </div>
    <div className="gl-stat-value">{value ?? "–"}</div>
    {sub && <div style={{ fontSize:12, color:"var(--txt3)", marginTop:5 }}>{sub}</div>}
  </div>
);

const Flash = ({ msg, type, onClose }) => {
  if (!msg) return null;
  const c = type==="error"
    ? { bg:"var(--redL)", border:"var(--red)", color:"var(--red)" }
    : { bg:"var(--greenL)", border:"var(--green)", color:"var(--green)" };
  return (
    <div style={{ background:c.bg, border:`1px solid ${c.border}`, borderRadius:10,
      padding:"11px 16px", fontSize:13, color:c.color, fontWeight:500,
      marginBottom:14, display:"flex", justifyContent:"space-between", alignItems:"center",
      flexWrap:"wrap", gap:10 }}>
      <span>{msg}</span>
      {onClose && <button onClick={onClose} style={{ background:"none", border:"none",
        color:c.color, fontSize:16, cursor:"pointer", opacity:.7 }}>×</button>}
    </div>
  );
};

/* ─── Chain Badge component ─────────────────────────────────────────────── */
const ChainBadge = ({ asset, onMint }) => {
  const [minting, setMinting] = useState(false);
  const [localTx, setLocalTx] = useState(null);

  const txHash = localTx || asset.blockchainTxHash;
  const isMinted = !!txHash;
  const isSimulated = asset.blockchainNetwork?.includes("Simulated");

  const handleMint = async (e) => {
    e.stopPropagation();
    setMinting(true);
    try {
      const res = await api.mintAsset(asset.id);
      setLocalTx(res.txHash);
      if (onMint) onMint(res);
    } catch {}
    setMinting(false);
  };

  if (minting) return (
    <div style={{ display:"flex", alignItems:"center", gap:6,
      color:"var(--chainG)", fontSize:11 }}>
      <Spinner size={11} /> Minting…
    </div>
  );

  if (!isMinted) return (
    <button onClick={handleMint} style={{
      background:"var(--chainL)", border:"1px dashed var(--chain)",
      color:"var(--chainG)", borderRadius:8, padding:"4px 10px",
      fontSize:11, fontWeight:600, cursor:"pointer",
      display:"flex", alignItems:"center", gap:5,
    }}>
      <span style={{ fontSize:12 }}>◈</span> Mint on Chain
    </button>
  );

  return (
    <div className="chain-pop" style={{ display:"flex", alignItems:"center", gap:6,
      background:"var(--chainL)", border:"1px solid var(--chain)",
      borderRadius:8, padding:"4px 10px" }}>
      <span style={{ fontSize:12 }}>◈</span>
      <span style={{ fontSize:11, fontWeight:600, color:"var(--chainG)" }}>
        {isSimulated ? "Simulated" : asset.blockchainNetwork || "Polygon"}
      </span>
      <a href={`https://mumbai.polygonscan.com/tx/${txHash}`}
         target="_blank" rel="noreferrer"
         style={{ fontFamily:"monospace", fontSize:10, color:"var(--purple)" }}
         onClick={e => e.stopPropagation()}>
        {short(txHash, 16)}
      </a>
    </div>
  );
};

/* ─── useApi hook ───────────────────────────────────────────────────────── */
function useApi(fn, deps = []) {
  const [data, setData]       = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError]     = useState(null);

  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try   { setData(await fn()); }
    catch (e) { setError(e.message); }
    finally   { setLoading(false); }
  // eslint-disable-next-line
  }, deps);

  useEffect(() => { load(); }, [load]);
  return { data, loading, error, reload: load };
}

/* ══════════════════════════════════════════════════════════════════════════
   PAGE: DASHBOARD
══════════════════════════════════════════════════════════════════════════ */
function Dashboard() {
  const { data: stats, loading, error, reload } = useApi(() => api.getDashboard());
  const { data: chainStatus } = useApi(() => api.getBlockchainStatus());

  if (loading) return <Loading />;
  if (error)   return <Err msg={error} />;

  const max = Math.max(...(stats.byPlatform || []).map(p => p.count), 1);

  return (
    <div className="fade-up">
      {/* Chain Status Banner */}
      {chainStatus && (
        <div style={{ background: chainStatus.isConfigured ? "var(--chainL)" : "rgba(255,255,255,.04)",
          border:`1px solid ${chainStatus.isConfigured ? "var(--chain)" : "var(--border)"}`,
          borderRadius:var_r2, padding:"12px 18px", marginBottom:20,
          display:"flex", alignItems:"center", gap:12, flexWrap:"wrap" }}>
          <span style={{ fontSize:20 }}>◈</span>
          <div>
            <div style={{ fontSize:13, fontWeight:600,
              color: chainStatus.isConfigured ? "var(--chainG)" : "var(--txt2)" }}>
              Blockchain: {chainStatus.mode}
            </div>
            <div style={{ fontSize:12, color:"var(--txt3)" }}>{chainStatus.message}</div>
          </div>
          {!chainStatus.isConfigured && (
            <div style={{ marginLeft:"auto", fontSize:11, color:"var(--txt3)" }}>
              Add keys to appsettings.json → switch to live minting
            </div>
          )}
        </div>
      )}

      {/* Stats row */}
      <div className="gl-dashboard-stats">
        <StatCard label="Protected Assets"  value={stats.totalAssets}      accent="var(--blue)"   icon="🛡" sub="Active registrations" />
        <StatCard label="Active Violations" value={stats.activeViolations} accent="var(--red)"    icon="⚠" sub="Pending action" />
        <StatCard label="Takedowns Sent"    value={stats.takedownsSent}    accent="var(--amber)"  icon="📬" sub="DMCA filed" />
        <StatCard label="Scans Today"       value={stats.scansRunToday}    accent="var(--green)"  icon="🔍" sub="Automated sweeps" />
      </div>

      {/* Platform chart + recent violations */}
      <div className="gl-dashboard-split">
        <Card>
          <SLabel>Violations by platform</SLabel>
          {(stats.byPlatform||[]).length === 0
            ? <div style={{ fontSize:13, color:"var(--txt3)" }}>No violations yet.</div>
            : (stats.byPlatform||[]).map(p => (
              <div key={p.platform} style={{ marginBottom:12 }}>
                <div style={{ display:"flex", justifyContent:"space-between", marginBottom:4 }}>
                  <span style={{ fontSize:13, color:"var(--txt2)" }}>{PLAT_I[p.platform]||"•"} {p.platform}</span>
                  <span style={{ fontSize:13, fontWeight:600 }}>{p.count}</span>
                </div>
                <div style={{ height:5, background:"var(--border)", borderRadius:3 }}>
                  <div style={{ height:5, background:`#${p.color}`, borderRadius:3,
                    width:`${(p.count/max)*100}%`, transition:"width .5s" }} />
                </div>
              </div>
            ))
          }
        </Card>

        <Card style={{ padding:0, overflow:"hidden" }}>
          <div style={{ padding:"16px 20px 12px", borderBottom:"1px solid var(--border)",
            display:"flex", justifyContent:"space-between", alignItems:"center", flexWrap:"wrap", gap:8 }}>
            <SLabel>Recent detections</SLabel>
            <Btn small variant="ghost" onClick={reload}>↻ Refresh</Btn>
          </div>
          {(stats.recentViolations||[]).length === 0
            ? <div style={{ padding:24, textAlign:"center", color:"var(--txt3)", fontSize:13 }}>No violations detected.</div>
            : <div className="gl-table-scroll">
            <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
                <thead>
                  <tr style={{ borderBottom:"1px solid var(--border)" }}>
                    {["Asset","Platform","Confidence","Status","Detected"].map(h => (
                      <th key={h} style={{ padding:"8px 16px", textAlign:"left", fontSize:10,
                        fontWeight:700, textTransform:"uppercase", letterSpacing:".07em",
                        color:"var(--txt3)" }}>{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {(stats.recentViolations||[]).map(v => (
                    <tr key={v.id} style={{ borderBottom:"1px solid var(--border)" }}>
                      <td style={{ padding:"10px 16px", fontWeight:500, maxWidth:180,
                        overflow:"hidden", textOverflow:"ellipsis", whiteSpace:"nowrap" }}>{v.assetTitle}</td>
                      <td style={{ padding:"10px 16px", color:"var(--txt2)" }}>{PLAT_I[v.platform]||"•"} {v.platform}</td>
                      <td style={{ padding:"10px 16px", fontWeight:700,
                        color:v.confidence>=.97?"var(--red)":v.confidence>=.90?"var(--amber)":"var(--blue)" }}>
                        {fmt(v.confidence)}
                      </td>
                      <td style={{ padding:"10px 16px" }}><StatusPill status={v.status} /></td>
                      <td style={{ padding:"10px 16px", color:"var(--txt3)" }}>{ago(v.detectedAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          }
        </Card>
      </div>
    </div>
  );
}

/* ══════════════════════════════════════════════════════════════════════════
   PAGE: ASSETS
══════════════════════════════════════════════════════════════════════════ */
function Assets() {
  const { data: assets, loading, error, reload } = useApi(() => api.getAssets());
  const [showUpload, setShowUpload] = useState(false);
  const [scanning, setScanning]     = useState({});
  const [flash, setFlash]           = useState(null);

  const flashMsg = (type, text) => {
    setFlash({ type, text });
    setTimeout(() => setFlash(null), 5000);
  };

  const handleScan = async (id) => {
    setScanning(s => ({ ...s, [id]: true }));
    try {
      await api.startScan(id);
      flashMsg("success", `Scan started for asset #${id}. Check Scan Jobs for results.`);
    } catch (e) { flashMsg("error", e.message); }
    finally { setScanning(s => ({ ...s, [id]: false })); }
  };

  if (loading) return <Loading />;
  if (error)   return <Err msg={error} />;

  return (
    <div className="fade-up">
      <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center", marginBottom:20, flexWrap:"wrap", gap:10 }}>
        <span style={{ fontSize:13, color:"var(--txt3)" }}>{assets?.length||0} assets registered</span>
        <Btn onClick={() => setShowUpload(true)} color="var(--blue)">+ Register New Asset</Btn>
      </div>

      {flash && <Flash msg={flash.text} type={flash.type} onClose={() => setFlash(null)} />}
      {showUpload && <UploadModal onClose={() => setShowUpload(false)} onSuccess={() => { setShowUpload(false); reload(); }} />}

      {(!assets||assets.length===0)
        ? <div style={{ textAlign:"center", padding:60, color:"var(--txt3)", fontSize:14 }}>
            No assets registered. Click "Register New Asset" to begin.
          </div>
        : <div style={{ display:"grid", gridTemplateColumns:"repeat(auto-fill,minmax(min(100%,280px),1fr))", gap:14 }}>
            {assets.map(a => (
              <Card key={a.id} style={{ display:"flex", flexDirection:"column", gap:12 }}>
                {/* Header */}
                <div style={{ display:"flex", justifyContent:"space-between", alignItems:"flex-start",
                  flexWrap:"wrap", gap:10 }}>
                  <div style={{ flex:1, minWidth:0 }}>
                    <Pill label={a.type} bg="var(--blueL)" color="var(--blue)" />
                    <div style={{ fontSize:14, fontWeight:600, marginTop:7, lineHeight:1.35,
                      overflow:"hidden", textOverflow:"ellipsis", whiteSpace:"nowrap" }}>{a.title}</div>
                    <div style={{ fontSize:12, color:"var(--txt3)", marginTop:3 }}>{a.organization}</div>
                  </div>
                  <Pill label={a.sport||"–"} bg="rgba(255,255,255,.05)" color="var(--txt2)" />
                </div>

                {/* pHash */}
                <div style={{ background:"var(--bg3)", borderRadius:8, padding:"8px 12px",
                  fontFamily:"monospace", fontSize:11, color:"var(--txt3)",
                  overflow:"hidden", textOverflow:"ellipsis", whiteSpace:"nowrap" }}>
                  <span style={{ color:"var(--txt3)" }}>pHash: </span>
                  <span style={{ color:"var(--cyan)" }}>{a.pHash}</span>
                </div>

                {/* Blockchain badge */}
                <ChainBadge asset={a} onMint={(r) => {
                  flashMsg("success", `Minted on ${r.network}! Tx: ${short(r.txHash)}`);
                  reload();
                }} />

                {/* Footer */}
                <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center",
                  marginTop:"auto", flexWrap:"wrap", gap:10 }}>
                  <span style={{ fontSize:12, color:"var(--txt3)" }}>
                    {(a.violations||[]).length} violation{(a.violations||[]).length!==1?"s":""}
                  </span>
                  <Btn small disabled={scanning[a.id]} onClick={() => handleScan(a.id)}
                    loading={scanning[a.id]}>
                    {scanning[a.id] ? "Starting…" : "Scan Now →"}
                  </Btn>
                </div>
              </Card>
            ))}
          </div>
      }
    </div>
  );
}

/* ─── Upload Modal ──────────────────────────────────────────────────────── */
function UploadModal({ onClose, onSuccess }) {
  const [form, setForm] = useState({ title:"", sport:"Cricket", organization:"", type:"Highlight" });
  const [imgFile, setImgFile]   = useState(null);
  const [preview, setPreview]   = useState(null);
  const [loading, setLoading]   = useState(false);
  const [error, setError]       = useState(null);
  const [minted, setMinted]     = useState(null);
  const fileRef = useRef();

  const set = (k,v) => setForm(f => ({...f,[k]:v}));

  const handleFile = (e) => {
    const file = e.target.files[0];
    if (!file) return;
    if (file.size > 10*1024*1024) { setError("Image must be under 10 MB"); return; }
    const r = new FileReader();
    r.onload = ev => { setImgFile(ev.target.result); setPreview(ev.target.result); setError(null); };
    r.readAsDataURL(file);
  };

  const handleSubmit = async () => {
    if (!form.title.trim())        { setError("Title is required"); return; }
    if (!form.organization.trim()) { setError("Organization is required"); return; }
    if (!imgFile)                  { setError("Please select an image"); return; }
    setLoading(true); setError(null);
    try {
      const asset = await api.registerAsset({
        title: form.title.trim(), sport: form.sport,
        organization: form.organization.trim(), type: form.type, base64Image: imgFile
      });
      // Poll for blockchain tx (fires in background on server)
      let attempts = 0;
      const pollChain = setInterval(async () => {
        attempts++;
        try {
          const fresh = await api.getAsset(asset.id);
          if (fresh?.blockchainTxHash) {
            setMinted({ txHash: fresh.blockchainTxHash, network: fresh.blockchainNetwork });
            clearInterval(pollChain);
          }
        } catch {}
        if (attempts > 15) clearInterval(pollChain);
      }, 1200);
      setTimeout(() => { onSuccess(); }, 3500);
    } catch (e) { setError(e.message); setLoading(false); }
  };

  return (
    <div style={{ position:"fixed", inset:0, background:"rgba(0,0,0,.7)",
      display:"flex", alignItems:"center", justifyContent:"center", zIndex:200, padding:16 }}>
      <div style={{ background:"var(--bg2)", border:"1px solid var(--border2)",
        borderRadius:16, width:"100%", maxWidth:500, maxHeight:"92vh",
        overflowY:"auto", animation:"fadeUp .2s ease" }}>

        {/* Header */}
        <div style={{ padding:"20px 24px 16px", borderBottom:"1px solid var(--border)",
          display:"flex", justifyContent:"space-between", alignItems:"flex-start" }}>
          <div>
            <div style={{ fontSize:17, fontWeight:700 }}>Register New Asset</div>
            <div style={{ fontSize:12, color:"var(--txt3)", marginTop:3 }}>
              pHash computed + watermark embedded + minted on Polygon automatically
            </div>
          </div>
          <button onClick={onClose} style={{ background:"none", border:"none",
            color:"var(--txt3)", fontSize:22, lineHeight:1, padding:"0 2px" }}>×</button>
        </div>

        <div style={{ padding:"20px 24px" }}>
          {error && <Flash msg={error} type="error" />}

          {/* Blockchain mint status */}
          {minted && (
            <div className="chain-pop" style={{ background:"var(--chainL)",
              border:"1px solid var(--chain)", borderRadius:10,
              padding:"12px 16px", marginBottom:16 }}>
              <div style={{ fontSize:13, fontWeight:600, color:"var(--chainG)", marginBottom:4 }}>
                ◈ Minted on {minted.network}
              </div>
              <div style={{ fontFamily:"monospace", fontSize:11, color:"var(--purple)" }}>
                {short(minted.txHash, 30)}
              </div>
              <a href={`https://mumbai.polygonscan.com/tx/${minted.txHash}`}
                 target="_blank" rel="noreferrer"
                 style={{ fontSize:11, color:"var(--blue)", marginTop:4, display:"block" }}>
                View on Polygonscan →
              </a>
            </div>
          )}

          {/* Image drop zone */}
          <div style={{ marginBottom:16 }}>
            <SLabel>Media File *</SLabel>
            <div onClick={() => fileRef.current.click()}
              style={{ border:`2px dashed ${imgFile ? "var(--chain)" : "var(--border2)"}`,
                borderRadius:10, padding:20, textAlign:"center", cursor:"pointer",
                background: imgFile ? "var(--chainL)" : "var(--bg3)", transition:"all .15s" }}>
              {preview
                ? <img src={preview} alt="preview"
                    style={{ maxHeight:140, maxWidth:"100%", borderRadius:8, objectFit:"contain" }} />
                : <div>
                    <div style={{ fontSize:28, marginBottom:8 }}>📁</div>
                    <div style={{ fontSize:13, color:"var(--txt3)" }}>
                      Click to select (JPG, PNG, WebP — max 10 MB)
                    </div>
                  </div>
              }
            </div>
            <input ref={fileRef} type="file" accept="image/*"
              style={{ display:"none" }} onChange={handleFile} />
            {imgFile && <button onClick={() => { setImgFile(null); setPreview(null); fileRef.current.value=""; }}
              style={{ marginTop:6, fontSize:11, color:"var(--txt3)", background:"none",
                border:"none", cursor:"pointer", textDecoration:"underline" }}>
              Remove image
            </button>}
          </div>

          {/* Title */}
          <div style={{ marginBottom:14 }}>
            <SLabel>Title *</SLabel>
            <input placeholder="e.g. IPL 2024 Final — Last Over Highlights"
              value={form.title} onChange={e => set("title", e.target.value)} />
          </div>

          {/* Org */}
          <div style={{ marginBottom:14 }}>
            <SLabel>Organization *</SLabel>
            <input placeholder="e.g. BCCI, Mashal Sports, FSDL"
              value={form.organization} onChange={e => set("organization", e.target.value)} />
          </div>

          {/* Sport + Type */}
          <div className="gl-form-two-col">
            <div>
              <SLabel>Sport</SLabel>
              <select value={form.sport} onChange={e => set("sport",e.target.value)}>
                {["Cricket","Football","Kabaddi","Hockey","Basketball","Tennis","Wrestling","Other"].map(s=><option key={s}>{s}</option>)}
              </select>
            </div>
            <div>
              <SLabel>Asset Type</SLabel>
              <select value={form.type} onChange={e => set("type",e.target.value)}>
                {["Image","VideoClip","Highlight","Broadcast"].map(t=><option key={t}>{t}</option>)}
              </select>
            </div>
          </div>

          {/* Info box */}
          <div style={{ background:"var(--blueL)", border:"1px solid rgba(59,130,246,.25)",
            borderRadius:9, padding:"11px 14px", fontSize:12, color:"var(--txt2)",
            marginBottom:18, lineHeight:1.7 }}>
            <span style={{ fontWeight:600, color:"var(--blue)" }}>What happens automatically:</span>
            <br/>1. 64-bit DCT pHash fingerprint computed from your image
            <br/>2. Invisible LSB watermark token embedded for ownership proof
            <br/>3. <span style={{ color:"var(--chainG)", fontWeight:500 }}>
              SHA-256 commitment hash minted to Polygon blockchain
            </span>
            <br/>4. Immutable timestamp recorded — nobody can dispute the registration date
          </div>

          <div className="gl-modal-actions">
            <Btn full onClick={handleSubmit} disabled={loading||!imgFile} loading={loading}
              color="var(--blue)">
              {loading ? "Registering + Minting…" : "Register & Protect Asset →"}
            </Btn>
            <Btn variant="ghost" onClick={onClose} disabled={loading}>Cancel</Btn>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ══════════════════════════════════════════════════════════════════════════
   PAGE: VIOLATIONS
══════════════════════════════════════════════════════════════════════════ */
function Violations() {
  const [filter, setFilter] = useState("all");
  const { data, loading, error, reload } = useApi(
    () => api.getViolations(filter==="all"?null:filter), [filter]);
  const [acting, setActing] = useState({});
  const [flash, setFlash]   = useState(null);

  const flashMsg = (type,text) => { setFlash({type,text}); setTimeout(()=>setFlash(null),5000); };

  const handleTakedown = async (id) => {
    setActing(a=>({...a,[id]:"takedown"}));
    try {
      const res = await api.sendTakedown(id);
      flashMsg("success", `Takedown sent! Reference: ${res.reference}${res.blockchainEvidenceTx ? ` | Chain evidence: ${short(res.blockchainEvidenceTx)}` : ""}`);
      reload();
    } catch(e) { flashMsg("error",e.message); }
    finally { setActing(a=>({...a,[id]:null})); }
  };

  const handleDismiss = async (id) => {
    setActing(a=>({...a,[id]:"dismiss"}));
    try { await api.dismissViolation(id); flashMsg("success","Dismissed."); reload(); }
    catch(e) { flashMsg("error",e.message); }
    finally { setActing(a=>({...a,[id]:null})); }
  };

  const violations = data || [];
  const tabs = [
    { id:"all",          label:`All (${violations.length})` },
    { id:"Detected",     label:"Detected" },
    { id:"UnderReview",  label:"Under Review" },
    { id:"TakedownSent", label:"Takedown Sent" },
    { id:"Resolved",     label:"Resolved" },
  ];

  return (
    <div className="fade-up">
      <div style={{ display:"flex", gap:6, marginBottom:16, flexWrap:"wrap", alignItems:"center", rowGap:8 }}>
        {tabs.map(t => (
          <button key={t.id} onClick={()=>setFilter(t.id)} style={{
            background: filter===t.id?"var(--blue)":"var(--bg2)",
            color: filter===t.id?"#fff":"var(--txt3)",
            border: `1px solid ${filter===t.id?"var(--blue)":"var(--border2)"}`,
            borderRadius:7, padding:"5px 14px", fontSize:12, fontWeight:500,
          }}>{t.label}</button>
        ))}
        <div style={{ marginLeft:"auto", flexShrink:0 }}>
          <Btn small variant="ghost" onClick={reload}>↻ Refresh</Btn>
        </div>
      </div>

      {flash && <Flash msg={flash.text} type={flash.type} onClose={()=>setFlash(null)} />}

      {loading ? <Loading /> : error ? <Err msg={error} /> : violations.length===0
        ? <div style={{ textAlign:"center", padding:60, color:"var(--txt3)", fontSize:14 }}>No violations found.</div>
        : <Card style={{ padding:0, overflow:"hidden" }}>
            <div className="gl-table-scroll gl-table-scroll--wide">
            <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
              <thead>
                <tr style={{ borderBottom:"1px solid var(--border)" }}>
                  {["Asset","Platform","URL","Match%","Status","Chain Evidence","Detected","Actions"].map(h=>(
                    <th key={h} style={{ padding:"10px 14px", textAlign:"left", fontSize:10,
                      fontWeight:700, textTransform:"uppercase", letterSpacing:".07em",
                      color:"var(--txt3)", background:"var(--bg3)", whiteSpace:"nowrap" }}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {violations.map(v => (
                  <tr key={v.id} style={{ borderBottom:"1px solid var(--border)" }}>
                    <td style={{ padding:"10px 14px", fontWeight:500, maxWidth:150,
                      overflow:"hidden", textOverflow:"ellipsis", whiteSpace:"nowrap" }}>
                      {v.digitalAsset?.title||`Asset #${v.digitalAssetId}`}
                    </td>
                    <td style={{ padding:"10px 14px", color:"var(--txt2)", whiteSpace:"nowrap" }}>
                      {PLAT_I[v.platform]||"•"} {v.platform}
                    </td>
                    <td style={{ padding:"10px 14px", maxWidth:160 }}>
                      <a href={v.infringingUrl} target="_blank" rel="noreferrer"
                        style={{ color:"var(--blue)", fontSize:11, display:"block",
                          overflow:"hidden", textOverflow:"ellipsis", whiteSpace:"nowrap" }}>
                        {v.infringingUrl}
                      </a>
                    </td>
                    <td style={{ padding:"10px 14px", fontWeight:700,
                      color:v.matchConfidence>=.97?"var(--red)":v.matchConfidence>=.90?"var(--amber)":"var(--blue)" }}>
                      {fmt(v.matchConfidence)}
                    </td>
                    <td style={{ padding:"10px 14px" }}><StatusPill status={v.status} /></td>
                    <td style={{ padding:"10px 14px" }}>
                      {v.blockchainEvidenceTx
                        ? <a href={`https://mumbai.polygonscan.com/tx/${v.blockchainEvidenceTx}`}
                             target="_blank" rel="noreferrer"
                             style={{ fontFamily:"monospace", fontSize:10, color:"var(--purple)",
                               display:"flex", alignItems:"center", gap:4 }}>
                            <span>◈</span> {short(v.blockchainEvidenceTx)}
                          </a>
                        : <span style={{ color:"var(--txt3)", fontSize:11 }}>–</span>
                      }
                    </td>
                    <td style={{ padding:"10px 14px", color:"var(--txt3)", whiteSpace:"nowrap" }}>
                      {ago(v.detectedAt)}
                    </td>
                    <td style={{ padding:"10px 14px" }}>
                      <div className="gl-action-row">
                        {v.status==="Detected" && (
                          <Btn small color="var(--red)"
                            disabled={acting[v.id]==="takedown"} loading={acting[v.id]==="takedown"}
                            onClick={()=>handleTakedown(v.id)}>Takedown</Btn>
                        )}
                        {(v.status==="Detected"||v.status==="UnderReview") && (
                          <Btn small variant="ghost"
                            disabled={acting[v.id]==="dismiss"} loading={acting[v.id]==="dismiss"}
                            onClick={()=>handleDismiss(v.id)}>Dismiss</Btn>
                        )}
                        {v.takedownReference && (
                          <span style={{ fontSize:10, color:"var(--txt3)", fontFamily:"monospace",
                            alignSelf:"center" }}>{v.takedownReference.slice(-8)}</span>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            </div>
          </Card>
      }
    </div>
  );
}

/* ══════════════════════════════════════════════════════════════════════════
   PAGE: BLOCKCHAIN
══════════════════════════════════════════════════════════════════════════ */
function Blockchain() {
  const { data: assets, loading: aLoading } = useApi(() => api.getAssets());
  const { data: status } = useApi(() => api.getBlockchainStatus());
  const [selected, setSelected]   = useState(null);
  const [proofData, setProofData] = useState(null);
  const [verifying, setVerifying] = useState(false);
  const [minting, setMinting]     = useState(null);
  const [flash, setFlash]         = useState(null);

  const flashMsg = (type,text) => { setFlash({type,text}); setTimeout(()=>setFlash(null),6000); };

  const handleVerify = async (assetId) => {
    setVerifying(true); setProofData(null);
    try { setProofData(await api.verifyOnChain(assetId)); }
    catch(e) { flashMsg("error", e.message); }
    setVerifying(false);
  };

  const handleMint = async (assetId) => {
    setMinting(assetId);
    try {
      const res = await api.mintAsset(assetId);
      flashMsg("success", `Minted on ${res.network}! Block: ${res.blockNumber}`);
      setProofData(null);
    } catch(e) { flashMsg("error", e.message); }
    setMinting(null);
  };

  const minted = (assets||[]).filter(a => a.blockchainTxHash);
  const unminted = (assets||[]).filter(a => !a.blockchainTxHash);

  return (
    <div className="fade-up">
      {flash && <Flash msg={flash.text} type={flash.type} onClose={()=>setFlash(null)} />}

      {/* Status card */}
      {status && (
        <Card glow={status.isConfigured} style={{ marginBottom:20, display:"flex", gap:20, alignItems:"center", flexWrap:"wrap" }}>
          <div style={{ width:48, height:48, borderRadius:12,
            background:"var(--chainL)", display:"flex", alignItems:"center",
            justifyContent:"center", fontSize:24, flexShrink:0 }}>◈</div>
          <div style={{ flex:1 }}>
            <div style={{ fontSize:15, fontWeight:600, color:"var(--chainG)", marginBottom:3 }}>
              {status.mode} — {status.network}
            </div>
            <div style={{ fontSize:13, color:"var(--txt2)" }}>{status.message}</div>
          </div>
          {!status.isConfigured && (
            <div style={{ background:"var(--bg3)", border:"1px solid var(--border2)",
              borderRadius:9, padding:"10px 14px", fontSize:12, color:"var(--txt3)",
              maxWidth:260 }}>
              To go live: deploy <code style={{ color:"var(--cyan)" }}>blockchain/GuardianLens.sol</code> on
              <a href="https://remix.ethereum.org" target="_blank" rel="noreferrer"
                style={{ color:"var(--blue)", marginLeft:4 }}>remix.ethereum.org</a>
              , then fill in <code style={{ color:"var(--cyan)" }}>appsettings.json</code>
            </div>
          )}
        </Card>
      )}

      {/* Stats */}
      <div className="gl-blockchain-stats">
        <StatCard label="Minted Assets"   value={minted.length}   accent="var(--chain)" icon="◈" sub="On-chain provenance" />
        <StatCard label="Unminted Assets" value={unminted.length} accent="var(--amber)"  icon="⏳" sub="Pending mint" />
        <StatCard label="Total Assets"    value={(assets||[]).length} accent="var(--blue)" icon="🛡" />
      </div>

      <div className="gl-blockchain-split">

        {/* Asset list */}
        <Card style={{ padding:0, overflow:"hidden" }}>
          <div style={{ padding:"14px 18px 12px", borderBottom:"1px solid var(--border)" }}>
            <SLabel>All assets — blockchain status</SLabel>
          </div>
          {aLoading ? <Loading /> : (
            <div style={{ overflowY:"auto", maxHeight:440 }}>
              {(assets||[]).map(a => (
                <div key={a.id}
                  onClick={()=>{ setSelected(a.id); setProofData(null); }}
                  style={{ padding:"12px 18px", borderBottom:"1px solid var(--border)",
                    cursor:"pointer", background: selected===a.id ? "var(--blueL)" : "transparent",
                    transition:"background .1s", display:"flex", alignItems:"center", gap:12 }}>
                  <div style={{ flex:1, minWidth:0 }}>
                    <div style={{ fontSize:13, fontWeight:500, overflow:"hidden",
                      textOverflow:"ellipsis", whiteSpace:"nowrap" }}>{a.title}</div>
                    <div style={{ fontSize:11, color:"var(--txt3)", marginTop:2 }}>{a.organization}</div>
                  </div>
                  {a.blockchainTxHash
                    ? <Pill label="Minted" bg="var(--chainL)" color="var(--chainG)" />
                    : <Pill label="Not minted" bg="var(--amberL)" color="var(--amber)" />
                  }
                </div>
              ))}
            </div>
          )}
        </Card>

        {/* Detail panel */}
        <Card>
          {!selected
            ? <div style={{ textAlign:"center", padding:40, color:"var(--txt3)", fontSize:13 }}>
                Select an asset to view its blockchain status
              </div>
            : (() => {
                const a = (assets||[]).find(x => x.id===selected);
                if (!a) return null;
                return (
                  <div>
                    <div style={{ fontSize:14, fontWeight:600, marginBottom:14 }}>{a.title}</div>

                    {/* pHash */}
                    <SLabel>Perceptual Hash (pHash)</SLabel>
                    <div style={{ fontFamily:"monospace", fontSize:12, color:"var(--cyan)",
                      background:"var(--bg3)", padding:"8px 12px", borderRadius:8, marginBottom:14 }}>
                      {a.pHash}
                    </div>

                    {/* Watermark token */}
                    <SLabel>Watermark Token</SLabel>
                    <div style={{ fontFamily:"monospace", fontSize:12, color:"var(--txt2)",
                      background:"var(--bg3)", padding:"8px 12px", borderRadius:8, marginBottom:14 }}>
                      {a.watermarkToken||"–"}
                    </div>

                    {/* Blockchain status */}
                    {a.blockchainTxHash ? (
                      <div style={{ background:"var(--chainL)", border:"1px solid var(--chain)",
                        borderRadius:10, padding:"14px 16px", marginBottom:14 }}>
                        <div style={{ fontSize:13, fontWeight:600, color:"var(--chainG)", marginBottom:10 }}>
                          ◈ Registered on {a.blockchainNetwork||"Blockchain"}
                        </div>
                        {[
                          ["Tx Hash",    a.blockchainTxHash],
                          ["Commitment", a.blockchainCommitmentHash],
                          ["Block",      a.blockchainBlockNumber?.toLocaleString()],
                          ["Minted",     a.blockchainTimestamp ? new Date(a.blockchainTimestamp).toLocaleString() : "–"],
                        ].map(([k,v]) => v && (
                          <div key={k} className="gl-kv-row">
                            <span>{k}:</span>
                            <span style={{ fontFamily:"monospace", color:"var(--purple)",
                              overflow:"hidden", textOverflow:"ellipsis" }}>
                              {k==="Tx Hash"||k==="Commitment" ? short(v,28) : v}
                            </span>
                          </div>
                        ))}
                        <a href={`https://mumbai.polygonscan.com/tx/${a.blockchainTxHash}`}
                           target="_blank" rel="noreferrer"
                           style={{ fontSize:12, color:"var(--blue)", marginTop:6, display:"block" }}>
                          View on Polygonscan →
                        </a>
                      </div>
                    ) : (
                      <div style={{ background:"var(--amberL)", border:"1px solid var(--amber)",
                        borderRadius:10, padding:"12px 16px", marginBottom:14 }}>
                        <div style={{ fontSize:13, color:"var(--amber)", fontWeight:600 }}>
                          Not yet minted on blockchain
                        </div>
                        <div style={{ fontSize:12, color:"var(--txt3)", marginTop:4 }}>
                          Click "Mint Now" to create an immutable on-chain record
                        </div>
                      </div>
                    )}

                    {/* Actions */}
                    <div className="gl-action-row">
                      {!a.blockchainTxHash && (
                        <Btn onClick={()=>handleMint(a.id)}
                          disabled={minting===a.id} loading={minting===a.id}
                          color="var(--chain)">
                          {minting===a.id ? "Minting…" : "◈ Mint Now"}
                        </Btn>
                      )}
                      <Btn variant="ghost" onClick={()=>handleVerify(a.id)}
                        disabled={verifying} loading={verifying}>
                        {verifying ? "Verifying…" : "Verify On-Chain"}
                      </Btn>
                    </div>

                    {/* Verification result */}
                    {proofData && (
                      <div className="chain-pop" style={{ marginTop:16, background:"var(--bg3)",
                        border:`1px solid ${proofData.exists ? "var(--green)" : "var(--red)"}`,
                        borderRadius:10, padding:"14px 16px" }}>
                        <div style={{ fontSize:13, fontWeight:600,
                          color: proofData.exists ? "var(--green)" : "var(--red)", marginBottom:10 }}>
                          {proofData.exists ? "✓ Ownership Verified On-Chain" : "✗ Not found on chain"}
                        </div>
                        {proofData.exists && [
                          ["Organisation", proofData.organisation],
                          ["Registered",   proofData.registeredAt ? new Date(proofData.registeredAt).toLocaleString() : "–"],
                          ["Network",      proofData.network],
                          ["Mode",         proofData.isSimulated ? "Simulated" : "Live"],
                        ].map(([k,v]) => (
                          <div key={k} style={{ display:"flex", gap:8, marginBottom:4, fontSize:12 }}>
                            <span style={{ color:"var(--txt3)", minWidth:100 }}>{k}:</span>
                            <span style={{ color:"var(--txt2)" }}>{v||"–"}</span>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                );
              })()
          }
        </Card>
      </div>
    </div>
  );
}

/* ══════════════════════════════════════════════════════════════════════════
   PAGE: ALERTS
══════════════════════════════════════════════════════════════════════════ */
function Alerts() {
  const { data, loading, error, reload } = useApi(() => api.getAlerts(50));
  const alerts = data || [];
  const ALERT_C = {
    AutoTakedown: { bg:"var(--redL)",   c:"var(--red)",   icon:"🤖" },
    HighAlert:    { bg:"var(--amberL)", c:"var(--amber)", icon:"🚨" },
    Watchlist:    { bg:"var(--greenL)", c:"var(--green)", icon:"👁" },
  };
  return (
    <div className="fade-up">
      <div className="gl-alerts-stats">
        <StatCard label="Auto-Takedowns" value={alerts.filter(a=>a.eventType==="AutoTakedown").length} accent="var(--red)" icon="🤖" />
        <StatCard label="High Alerts"    value={alerts.filter(a=>a.eventType==="HighAlert").length}    accent="var(--amber)" icon="🚨" />
        <StatCard label="Watchlist"      value={alerts.filter(a=>a.eventType==="Watchlist").length}    accent="var(--green)" icon="👁" />
      </div>
      <div style={{ display:"flex", justifyContent:"space-between", marginBottom:12, alignItems:"center", flexWrap:"wrap", gap:8 }}>
        <SLabel>Alert engine event log</SLabel>
        <Btn small variant="ghost" onClick={reload}>↻ Refresh</Btn>
      </div>
      {loading ? <Loading /> : error ? <Err msg={error} /> : alerts.length===0
        ? <div style={{ textAlign:"center", padding:60, color:"var(--txt3)", fontSize:14 }}>No alert events yet.</div>
        : <div style={{ display:"flex", flexDirection:"column", gap:10 }}>
            {alerts.map(a => {
              const s = ALERT_C[a.eventType]||{bg:"var(--bg3)",c:"var(--txt3)",icon:"•"};
              return (
                <Card key={a.id} style={{ display:"flex", alignItems:"flex-start", gap:14, padding:"14px 18px",
                  flexWrap:"wrap" }}>
                  <div style={{ width:36,height:36,borderRadius:9,background:s.bg,
                    display:"flex",alignItems:"center",justifyContent:"center",fontSize:18,flexShrink:0 }}>
                    {s.icon}
                  </div>
                  <div style={{ flex:1, minWidth:0 }}>
                    <div className="gl-alerts-card-head">
                      <Pill label={a.eventType} bg={s.bg} color={s.c} />
                      <span style={{ fontSize:11, color:"var(--txt3)" }}>{ago(a.occurredAt)}</span>
                    </div>
                    <div style={{ fontSize:13, color:"var(--txt2)", lineHeight:1.5 }}>{a.message}</div>
                    <div style={{ fontSize:11, color:"var(--txt3)", marginTop:3 }}>Violation #{a.violationId}</div>
                  </div>
                </Card>
              );
            })}
          </div>
      }
    </div>
  );
}

/* ══════════════════════════════════════════════════════════════════════════
   PAGE: SCAN JOBS
══════════════════════════════════════════════════════════════════════════ */
function ScanJobs() {
  const { data, loading, error, reload } = useApi(() => api.getAllScans());
  const scans = data||[];
  return (
    <div className="fade-up">
      <div style={{ display:"flex", justifyContent:"flex-end", marginBottom:14 }}>
        <Btn small variant="ghost" onClick={reload}>↻ Refresh</Btn>
      </div>
      {loading ? <Loading /> : error ? <Err msg={error} /> : scans.length===0
        ? <div style={{ textAlign:"center", padding:60, color:"var(--txt3)", fontSize:14 }}>
            No scan jobs yet. Go to Assets and click Scan Now.
          </div>
        : <Card style={{ padding:0, overflow:"hidden" }}>
            <div className="gl-table-scroll">
            <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
              <thead>
                <tr style={{ borderBottom:"1px solid var(--border)" }}>
                  {["Asset","Status","URLs Scanned","Violations","Started","Duration"].map(h=>(
                    <th key={h} style={{ padding:"10px 14px", textAlign:"left", fontSize:10,
                      fontWeight:700, textTransform:"uppercase", letterSpacing:".07em",
                      color:"var(--txt3)", background:"var(--bg3)" }}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {scans.map(j => (
                  <tr key={j.id} style={{ borderBottom:"1px solid var(--border)" }}>
                    <td style={{ padding:"10px 14px", fontWeight:500, maxWidth:200,
                      overflow:"hidden", textOverflow:"ellipsis", whiteSpace:"nowrap" }}>
                      {j.digitalAsset?.title||`Asset #${j.digitalAssetId}`}
                    </td>
                    <td style={{ padding:"10px 14px" }}>
                      <Pill label={j.status}
                        bg={`rgba(${j.status==="Completed"?"34,197,94":j.status==="Running"?"59,130,246":"239,68,68"},.15)`}
                        color={SCAN_C[j.status]||"var(--txt3)"} />
                    </td>
                    <td style={{ padding:"10px 14px", fontWeight:600 }}>
                      {j.urlsScanned?.toLocaleString()||"–"}
                    </td>
                    <td style={{ padding:"10px 14px", fontWeight:700,
                      color:j.violationsFound>0?"var(--red)":"var(--green)" }}>
                      {j.violationsFound??0}
                    </td>
                    <td style={{ padding:"10px 14px", color:"var(--txt3)", whiteSpace:"nowrap" }}>
                      {ago(j.createdAt)}
                    </td>
                    <td style={{ padding:"10px 14px", color:"var(--txt3)" }}>
                      {dur(j.createdAt, j.completedAt)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            </div>
          </Card>
      }
    </div>
  );
}

/* ══════════════════════════════════════════════════════════════════════════
   PAGE: VERIFY OWNERSHIP
══════════════════════════════════════════════════════════════════════════ */
function Verify() {
  const [img, setImg]   = useState(null);
  const [prev, setPrev] = useState(null);
  const [res, setRes]   = useState(null);
  const [loading, setLoading] = useState(false);
  const [err, setErr]   = useState(null);
  const ref = useRef();

  const handleFile = (e) => {
    const f = e.target.files[0]; if (!f) return;
    const r = new FileReader();
    r.onload = ev => { setImg(ev.target.result); setPrev(ev.target.result); setRes(null); };
    r.readAsDataURL(f);
  };
  const check = async () => {
    if (!img) return;
    setLoading(true); setErr(null); setRes(null);
    try { setRes(await api.verifyImage(img)); }
    catch(e) { setErr(e.message); }
    setLoading(false);
  };
  return (
    <div className="fade-up" style={{ maxWidth:500, margin:"0 auto", paddingTop:8 }}>
      <Card>
        <div style={{ fontSize:17, fontWeight:700, marginBottom:4 }}>Verify Asset Ownership</div>
        <div style={{ fontSize:13, color:"var(--txt3)", marginBottom:20, lineHeight:1.6 }}>
          Upload any image to check it against the registered asset registry using
          pHash matching and watermark extraction.
        </div>
        <div onClick={()=>ref.current.click()}
          style={{ border:`2px dashed ${img?"var(--chain)":"var(--border2)"}`,
            borderRadius:10, padding:22, textAlign:"center", cursor:"pointer",
            background: img?"var(--chainL)":"var(--bg3)", marginBottom:16, transition:"all .15s" }}>
          {prev
            ? <img src={prev} alt="preview"
                style={{ maxHeight:150, maxWidth:"100%", borderRadius:8, objectFit:"contain" }} />
            : <div>
                <div style={{ fontSize:30, marginBottom:8 }}>🔍</div>
                <div style={{ fontSize:13, color:"var(--txt3)" }}>Click to select an image</div>
              </div>
          }
        </div>
        <input ref={ref} type="file" accept="image/*" style={{ display:"none" }} onChange={handleFile} />
        {img && <button onClick={()=>{setImg(null);setPrev(null);setRes(null);ref.current.value="";}}
          style={{ fontSize:11, color:"var(--txt3)", background:"none", border:"none",
            cursor:"pointer", textDecoration:"underline", marginBottom:12, display:"block" }}>
          Remove image</button>}

        {err && <Flash msg={err} type="error" />}
        <Btn full onClick={check} disabled={!img||loading} loading={loading} color="var(--blue)">
          {loading ? "Checking pHash + Watermark…" : "Check Ownership →"}
        </Btn>

        {res && (
          <div className="chain-pop" style={{ marginTop:18, padding:18, borderRadius:10,
            background: res.isMatch?"var(--amberL)":"var(--greenL)",
            border:`1px solid ${res.isMatch?"var(--amber)":"var(--green)"}` }}>
            {res.isMatch ? (
              <>
                <div style={{ fontWeight:700, color:"var(--amber)", fontSize:15, marginBottom:10 }}>
                  ⚠ Registered Asset Match Found
                </div>
                {[
                  ["Asset",      res.assetTitle],
                  ["Owner",      res.organization],
                  ["Confidence", `${res.confidence}%`],
                  ["Watermark",  res.watermarkToken],
                  ["pHash",      res.queryHash],
                ].map(([k,v]) => v && (
                  <div key={k} className="gl-kv-row" style={{ fontSize:13 }}>
                    <span>{k}:</span>
                    <span style={{ fontFamily: k==="Watermark"||k==="pHash"?"monospace":undefined,
                      fontSize: k==="Watermark"||k==="pHash"?11:13,
                      color:"var(--txt)", wordBreak:"break-all" }}>{v}</span>
                  </div>
                ))}
              </>
            ) : (
              <div style={{ fontWeight:600, color:"var(--green)", fontSize:14 }}>
                ✓ No match — this image is not in the protected registry
              </div>
            )}
          </div>
        )}
      </Card>
    </div>
  );
}

/* ══════════════════════════════════════════════════════════════════════════
   ROOT APP
══════════════════════════════════════════════════════════════════════════ */
const PAGES = [
  { id:"dashboard",  label:"Dashboard",       icon:"⬡" },
  { id:"assets",     label:"Assets",          icon:"🛡" },
  { id:"violations", label:"Violations",      icon:"⚠" },
  { id:"blockchain", label:"Blockchain",      icon:"◈" },
  { id:"alerts",     label:"Alerts",          icon:"🔔" },
  { id:"scanjobs",   label:"Scan Jobs",       icon:"🔍" },
  { id:"verify",     label:"Verify",          icon:"✓" },
];

export default function App() {
  const [page, setPage] = useState("dashboard");
  const [online, setOnline] = useState(null);

  useEffect(() => {
    const check = () =>
      fetch("/api/assets/dashboard")
        .then(()=>setOnline(true))
        .catch(()=>setOnline(false));
    check();
    const t = setInterval(check, 12000);
    return () => clearInterval(t);
  }, []);

  return (
    <>
      <style>{GLOBAL_CSS}</style>
      <div style={{ display:"flex", flexDirection:"column", minHeight:"100vh" }}>

        {/* Topbar */}
        <header className="gl-header" style={{ background:"var(--bg2)", borderBottom:"1px solid var(--border)",
          position:"sticky", top:0, zIndex:100 }}>

          <div className="gl-brand" style={{ display:"flex", alignItems:"center", gap:9, flexShrink:0 }}>
            <div style={{ width:32, height:32, background:"var(--blue)", borderRadius:8,
              display:"flex", alignItems:"center", justifyContent:"center",
              fontSize:17, fontWeight:700, color:"#fff" }}>G</div>
            <span style={{ fontWeight:700, fontSize:15, letterSpacing:-.3 }}>GuardianLens</span>
            <span style={{ fontSize:11, color:"var(--txt3)", marginLeft:2 }}>v2</span>
          </div>

          <nav className="gl-nav">
            {PAGES.map(p => (
              <button key={p.id} onClick={()=>setPage(p.id)} style={{
                background: page===p.id?"var(--blueL)":"transparent",
                color: page===p.id?"var(--blue)":"var(--txt3)",
                border: `1px solid ${page===p.id?"rgba(59,130,246,.3)":"transparent"}`,
                borderRadius:7, padding:"5px 13px", fontSize:12, fontWeight:500,
                whiteSpace:"nowrap", transition:"all .15s",
              }}>
                <span style={{ marginRight:5, fontSize:12 }}>{p.icon}</span>{p.label}
              </button>
            ))}
          </nav>

          <div style={{ display:"flex", alignItems:"center", gap:8, flexShrink:0 }}>
            <div style={{ width:7, height:7, borderRadius:"50%",
              background: online===null?"var(--amber)":online?"var(--green)":"var(--red)",
              animation: online===null||online===false ? "pulse 1.5s ease-in-out infinite" : undefined }} />
            <span style={{ fontSize:11, color:"var(--txt3)" }}>
              {online===null?"Connecting…":online?"API Connected":"API Offline"}
            </span>
          </div>
        </header>

        {/* Offline banner */}
        {online===false && (
          <div className="gl-offline-banner" style={{ background:"var(--redL)", borderBottom:"1px solid var(--red)",
            color:"var(--red)", fontWeight:500 }}>
            ⚠ Backend offline —&nbsp;
            <code style={{ background:"rgba(255,255,255,.07)", padding:"2px 7px", borderRadius:4, fontSize:12 }}>
              cd GuardianLens.API → dotnet run
            </code>
          </div>
        )}

        {/* Page */}
        <main className="gl-main">
          <h1 className="gl-page-title">
            <span style={{ fontSize:20 }}>{PAGES.find(p=>p.id===page)?.icon}</span>
            {PAGES.find(p=>p.id===page)?.label}
          </h1>
          {page==="dashboard"  && <Dashboard />}
          {page==="assets"     && <Assets />}
          {page==="violations" && <Violations />}
          {page==="blockchain" && <Blockchain />}
          {page==="alerts"     && <Alerts />}
          {page==="scanjobs"   && <ScanJobs />}
          {page==="verify"     && <Verify />}
        </main>
      </div>
    </>
  );
}
