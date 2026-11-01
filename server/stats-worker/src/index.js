// Contador anónimo de uso de Muesca (antes ClaudeBar).
// La app manda como mucho un aviso al día, sin ningún identificador:
//   POST /v1/ping  {"v":"0.5.0","e":"free","l":"es","d":1,"m":1,"n":0}
//     v = versión, e = edición (free | pro | github), l = idioma,
//     d = primer aviso de hoy, m = primer aviso de este mes, n = instalación nueva.
// Aquí solo se suman casillas por día. No se guarda la IP ni nada que identifique a nadie,
// y no hay registros (observability desactivado en wrangler.jsonc).
// Las cifras se leen con GET /v1/stats?days=30 y la cabecera Authorization: Bearer <STATS_TOKEN>.

const EDITIONS = new Set(["free", "pro", "github"]);
const VERSION_RE = /^\d{1,3}\.\d{1,3}\.\d{1,4}(?:-[0-9A-Za-z.]{1,20})?$/;
const LANG_RE = /^[a-z]{2,3}(?:-[A-Z]{2})?$/;
const MAX_BODY = 512;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    try {
      if (url.pathname === "/v1/ping" && request.method === "POST") return await ping(request, env);
      if (url.pathname === "/v1/stats" && request.method === "GET") return await stats(request, env, url);
      if (url.pathname === "/" && request.method === "GET")
        return text("Contador anónimo de Muesca. No guarda IPs ni identificadores.", 200);
      return text("No encontrado", 404);
    } catch {
      return text("Error", 500);
    }
  },
};

async function ping(request, env) {
  // La IP solo sirve de clave efímera del limitador de Cloudflare; no se escribe en ningún sitio.
  if (env.LIMITER) {
    const ip = request.headers.get("cf-connecting-ip") ?? "";
    const { success } = await env.LIMITER.limit({ key: ip });
    if (!success) return new Response(null, { status: 429 });
  }

  const len = Number(request.headers.get("content-length") ?? "0");
  if (len > MAX_BODY) return new Response(null, { status: 413 });
  const raw = await request.text();
  if (raw.length > MAX_BODY) return new Response(null, { status: 413 });

  let body;
  try { body = JSON.parse(raw); } catch { return new Response(null, { status: 400 }); }
  const p = validate(body);
  if (!p) return new Response(null, { status: 400 });

  const day = new Date().toISOString().slice(0, 10);
  await env.DB.prepare(
    `INSERT INTO counts (day, version, edition, lang, daily, monthly, installs, pings)
     VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, 1)
     ON CONFLICT (day, version, edition, lang) DO UPDATE SET
       daily = daily + excluded.daily,
       monthly = monthly + excluded.monthly,
       installs = installs + excluded.installs,
       pings = pings + 1`
  ).bind(day, p.v, p.e, p.l, p.d, p.m, p.n).run();

  return new Response(null, { status: 204 });
}

function validate(b) {
  if (!b || typeof b !== "object" || Array.isArray(b)) return null;
  const flag = (x) => (x === 0 || x === 1 ? x : null);
  const v = typeof b.v === "string" && VERSION_RE.test(b.v) ? b.v : null;
  const e = typeof b.e === "string" && EDITIONS.has(b.e) ? b.e : null;
  const l = typeof b.l === "string" && LANG_RE.test(b.l) ? b.l : null;
  const d = flag(b.d), m = flag(b.m), n = flag(b.n);
  if (v === null || e === null || l === null || d === null || m === null || n === null) return null;
  return { v, e, l, d, m, n };
}

async function stats(request, env, url) {
  const auth = request.headers.get("authorization") ?? "";
  if (!env.STATS_TOKEN || !timingSafeEqual(auth, `Bearer ${env.STATS_TOKEN}`))
    return new Response(null, { status: 401 });

  const days = Math.min(Math.max(parseInt(url.searchParams.get("days") ?? "30", 10) || 30, 1), 400);
  const since = new Date(Date.now() - (days - 1) * 86400000).toISOString().slice(0, 10);
  const q = (sql) => env.DB.prepare(sql).bind(since).all().then((r) => r.results);

  const [perDay, perMonth, byEdition, byVersion, byLang] = await Promise.all([
    q(`SELECT day, SUM(daily) AS activos, SUM(installs) AS nuevas, SUM(pings) AS avisos
       FROM counts WHERE day >= ?1 GROUP BY day ORDER BY day`),
    q(`SELECT substr(day, 1, 7) AS mes, SUM(monthly) AS activos, SUM(installs) AS nuevas
       FROM counts WHERE day >= ?1 GROUP BY mes ORDER BY mes`),
    q(`SELECT edition, SUM(monthly) AS activos_mes FROM counts WHERE day >= ?1 GROUP BY edition ORDER BY activos_mes DESC`),
    q(`SELECT version, SUM(daily) AS activos_dia FROM counts WHERE day >= ?1 GROUP BY version ORDER BY activos_dia DESC`),
    q(`SELECT lang, SUM(monthly) AS activos_mes FROM counts WHERE day >= ?1 GROUP BY lang ORDER BY activos_mes DESC`),
  ]);

  return new Response(JSON.stringify({ desde: since, por_dia: perDay, por_mes: perMonth, por_edicion: byEdition, por_version: byVersion, por_idioma: byLang }, null, 2), {
    headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" },
  });
}

function timingSafeEqual(a, b) {
  if (a.length !== b.length) return false;
  let r = 0;
  for (let i = 0; i < a.length; i++) r |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return r === 0;
}

function text(s, status) {
  return new Response(s, { status, headers: { "content-type": "text/plain; charset=utf-8" } });
}
