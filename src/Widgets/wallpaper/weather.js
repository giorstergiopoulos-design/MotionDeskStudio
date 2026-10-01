// MotionDesk Studio — "Weather" wallpaper: a procedural scene driven by the real time of day (sun/moon/stars,
// sunrise & sunset computed from the location) and the live weather (clouds, rain, snow, fog, thunderstorms).
// Everything is drawn with Canvas 2D — no external assets, no network access from this file (the C# bridge supplies the weather).
// Public API (used by index.html): WeatherWallpaper.setConfig(cfg), .setWeather(data), .resize(w, h, dpr), .draw(dtSeconds, profile).
(function () {
'use strict';

const TAU = Math.PI * 2;
const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
const lerp = (a, b, t) => a + (b - a) * t;
const smooth = (t) => { t = clamp(t, 0, 1); return t * t * (3 - 2 * t); };

function mulberry32(seed) {
    return function () {
        seed |= 0; seed = (seed + 0x6D2B79F5) | 0;
        let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
        t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

function hex(h) {
    const s = h.replace('#', '');
    const n = parseInt(s, 16);
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}
const mix = (a, b, t) => [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];
const rgba = (c, a) => `rgba(${c[0] | 0},${c[1] | 0},${c[2] | 0},${a})`;
const lum = (c) => 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2];
const scaleCol = (c, k) => [c[0] * k, c[1] * k, c[2] * k];

// Beaufort scale (continuous 0..12) from km/h — the thresholds are the standard upper limits of Bf 0..11
const BF_LIMITS = [1, 6, 12, 20, 29, 39, 50, 62, 75, 89, 103, 118];
function beaufort(kmh) {
    if (kmh < 1) return Math.max(0, kmh);
    let i = 0;
    while (i < BF_LIMITS.length && kmh >= BF_LIMITS[i]) i++;
    if (i >= BF_LIMITS.length) return 12;
    const lo = BF_LIMITS[i - 1], hi = BF_LIMITS[i];
    return i + (kmh - lo) / (hi - lo);
}
let BFN = 2;           // current (eased) Beaufort number, updated every frame

// ---------------------------------------------------------------- state
let W = 0, H = 0, DPR = 1, HORIZON = 0, K = 1;
let cfg = { weatherSim: 'Auto', timeSim: 'Auto', glass: true, lat: 37.9838, lon: 23.7275 };
let data = null;                       // last weather payload from the host
let animTime = 0;
const S = { cloud: 0.15, rain: 0, snow: 0, fog: 0, storm: 0, windX: 0, wind: 8, dir: 1, snowCover: 0, wet: 0 };   // eased, current values
const TGT = { cloud: 0.15, rain: 0, snow: 0, fog: 0, storm: 0, windX: 0, wind: 8, dir: 1 };                           // targets from the weather

// offscreen canvases
let skyCv = null, skyCtx = null;       // sky + celestial + clouds + hills  (W x HORIZON)
let cloudCv = null, cloudCtx = null;   // half-res cloud layer
let sceneCv = null, sceneCtx = null;   // full composite (only when glass drops are drawn)
let cloudSprites = [], clouds = [], hills = [], stars = [], flakeSprite = null, fogSprites = [];
let rain = { far: [], mid: [], near: [] }, ripples = [], splashes = [], glass = [], bolt = null;
let nextBoltIn = 4, flash = 0, shoot = null, nextShootIn = 8;
let lastDensity = -1;

function makeCanvas(w, h) {
    const c = document.createElement('canvas');
    c.width = Math.max(1, Math.round(w)); c.height = Math.max(1, Math.round(h));
    return c;
}

// ---------------------------------------------------------------- sun / moon astronomy
function dayOfYear(d) {
    const start = Date.UTC(d.getUTCFullYear(), 0, 0);
    return Math.floor((d.getTime() - start) / 86400000);
}

let sunCache = { key: '', sr: 390, ss: 1170 };
function sunTimes(offMin) {
    const d = new Date();
    const key = `${d.getUTCFullYear()}-${dayOfYear(d)}-${cfg.lat}-${cfg.lon}-${offMin}`;
    if (key === sunCache.key) return sunCache;
    const rad = Math.PI / 180, lat = cfg.lat * rad;
    const gamma = TAU / 365 * (dayOfYear(d) - 1 + 0.5);
    const eqtime = 229.18 * (0.000075 + 0.001868 * Math.cos(gamma) - 0.032077 * Math.sin(gamma) - 0.014615 * Math.cos(2 * gamma) - 0.040849 * Math.sin(2 * gamma));
    const decl = 0.006918 - 0.399912 * Math.cos(gamma) + 0.070257 * Math.sin(gamma) - 0.006758 * Math.cos(2 * gamma) + 0.000907 * Math.sin(2 * gamma) - 0.002697 * Math.cos(3 * gamma) + 0.00148 * Math.sin(3 * gamma);
    const c = Math.cos(90.833 * rad) / (Math.cos(lat) * Math.cos(decl)) - Math.tan(lat) * Math.tan(decl);
    let sr = 390, ss = 1170;                                   // fallback 06:30 / 19:30 (also for polar day/night)
    if (c > -1 && c < 1) {
        const ha = Math.acos(c) / rad;
        const norm = (m) => ((m % 1440) + 1440) % 1440;
        sr = norm(720 - 4 * (cfg.lon + ha) - eqtime + offMin);
        ss = norm(720 - 4 * (cfg.lon - ha) - eqtime + offMin);
        if (ss < sr) ss += 1440;                               // sunset after midnight (very high offsets)
    }
    sunCache = { key, sr, ss };
    return sunCache;
}

function offsetMinutes() {
    if (data && typeof data.utcOffsetSec === 'number') return data.utcOffsetSec / 60;
    return -new Date().getTimezoneOffset();
}

function localMinutes() {
    const sim = cfg.timeSim;
    const { sr, ss } = sunTimes(offsetMinutes());
    if (sim === 'Dawn') return sr + 8;
    if (sim === 'Day') return (sr + ss) / 2;
    if (sim === 'Dusk') return ss - 4;
    if (sim === 'Night') return 60;
    const d = new Date();
    const utc = d.getUTCHours() * 60 + d.getUTCMinutes() + d.getUTCSeconds() / 60;
    return (utc + offsetMinutes() + 1440) % 1440;
}

// 0 = new moon, 0.5 = full moon (synodic month from a known new moon, 2000-01-06 18:14 UTC)
function moonPhase() {
    const days = (Date.now() - Date.UTC(2000, 0, 6, 18, 14)) / 86400000;
    return (((days / 29.530588853) % 1) + 1) % 1;
}

// ---------------------------------------------------------------- sky palette
const NIGHT = { top: hex('#04070f'), mid: hex('#070d1f'), hor: hex('#0e1a34'), amb: 0.05, glow: 0 };
function skyKeys(sr, ss) {
    const noon = (sr + ss) / 2;
    return [
        { t: sr - 100, ...NIGHT },
        { t: sr - 45, top: hex('#101a3c'), mid: hex('#2c3869'), hor: hex('#6f5079'), amb: 0.14, glow: 0.25 },
        { t: sr - 8,  top: hex('#27427e'), mid: hex('#9a7aa6'), hor: hex('#ff9d6b'), amb: 0.4, glow: 0.9 },
        { t: sr + 35, top: hex('#3a78c4'), mid: hex('#8fb6d9'), hor: hex('#ffd9a8'), amb: 0.72, glow: 0.6 },
        { t: sr + 120, top: hex('#2b79d0'), mid: hex('#5da7ec'), hor: hex('#c9e4f7'), amb: 0.93, glow: 0.2 },
        { t: noon,    top: hex('#1f6dce'), mid: hex('#4fa0ee'), hor: hex('#bfe0f8'), amb: 1, glow: 0.1 },
        { t: ss - 120, top: hex('#2b79d0'), mid: hex('#5da7ec'), hor: hex('#c9e4f7'), amb: 0.93, glow: 0.2 },
        { t: ss - 35, top: hex('#3a6bb8'), mid: hex('#d79a7e'), hor: hex('#ffc185'), amb: 0.72, glow: 0.7 },
        { t: ss + 2,  top: hex('#2a3f80'), mid: hex('#a65f7c'), hor: hex('#ff7f55'), amb: 0.4, glow: 1 },
        { t: ss + 40, top: hex('#141e4a'), mid: hex('#4a3a70'), hor: hex('#b0556c'), amb: 0.15, glow: 0.4 },
        { t: ss + 95, ...NIGHT },
    ];
}

function skyAt(t, sr, ss) {
    const keys = skyKeys(sr, ss);
    let tt = t;
    for (const cand of [t, t + 1440, t - 1440]) {
        if (cand >= keys[0].t && cand <= keys[keys.length - 1].t) { tt = cand; break; }
    }
    if (tt < keys[0].t || tt > keys[keys.length - 1].t) return { ...NIGHT };
    for (let i = 0; i < keys.length - 1; i++) {
        const a = keys[i], b = keys[i + 1];
        if (tt >= a.t && tt <= b.t) {
            const f = smooth((tt - a.t) / Math.max(1, b.t - a.t));
            return { top: mix(a.top, b.top, f), mid: mix(a.mid, b.mid, f), hor: mix(a.hor, b.hor, f), amb: lerp(a.amb, b.amb, f), glow: lerp(a.glow, b.glow, f) };
        }
    }
    return { ...NIGHT };
}

// ---------------------------------------------------------------- weather -> scene targets
function sceneFromCode(code) {
    switch (code) {
        case 0: return { cloud: 0.04, rain: 0, snow: 0, fog: 0, storm: 0 };
        case 1: return { cloud: 0.2, rain: 0, snow: 0, fog: 0, storm: 0 };
        case 2: return { cloud: 0.48, rain: 0, snow: 0, fog: 0, storm: 0 };
        case 3: return { cloud: 0.92, rain: 0, snow: 0, fog: 0, storm: 0 };
        case 45: case 48: return { cloud: 0.6, rain: 0, snow: 0, fog: 0.85, storm: 0 };
        case 51: case 53: case 55: case 56: case 57: return { cloud: 0.82, rain: code === 51 ? 0.18 : code === 53 ? 0.3 : 0.42, snow: 0, fog: 0.12, storm: 0 };
        case 61: return { cloud: 0.85, rain: 0.42, snow: 0, fog: 0.08, storm: 0 };
        case 63: return { cloud: 0.9, rain: 0.72, snow: 0, fog: 0.1, storm: 0 };
        case 65: return { cloud: 1, rain: 1, snow: 0, fog: 0.15, storm: 0 };
        case 66: case 67: return { cloud: 0.92, rain: 0.6, snow: 0, fog: 0.1, storm: 0 };
        case 71: return { cloud: 0.8, rain: 0, snow: 0.35, fog: 0.1, storm: 0 };
        case 73: return { cloud: 0.88, rain: 0, snow: 0.65, fog: 0.15, storm: 0 };
        case 75: return { cloud: 1, rain: 0, snow: 1, fog: 0.25, storm: 0 };
        case 77: return { cloud: 0.8, rain: 0, snow: 0.45, fog: 0.1, storm: 0 };
        case 80: return { cloud: 0.7, rain: 0.5, snow: 0, fog: 0, storm: 0 };
        case 81: return { cloud: 0.85, rain: 0.78, snow: 0, fog: 0.05, storm: 0 };
        case 82: return { cloud: 1, rain: 1, snow: 0, fog: 0.1, storm: 0 };
        case 85: return { cloud: 0.8, rain: 0, snow: 0.55, fog: 0.1, storm: 0 };
        case 86: return { cloud: 0.95, rain: 0, snow: 0.95, fog: 0.2, storm: 0 };
        case 95: return { cloud: 1, rain: 0.85, snow: 0, fog: 0.1, storm: 1 };
        case 96: case 99: return { cloud: 1, rain: 1, snow: 0, fog: 0.12, storm: 1 };
        default: return { cloud: 0.3, rain: 0, snow: 0, fog: 0, storm: 0 };
    }
}

const SIM_CODES = { Clear: 0, PartlyCloudy: 2, Cloudy: 3, Drizzle: 51, Rain: 63, HeavyRain: 65, Thunderstorm: 95, Snow: 73, Fog: 45 };

const WIND_SIM_KMH = { Calm: 3, LightBreeze: 15, FreshBreeze: 30, Strong: 52, Gale: 82 };
const SIM_WIND_KMH = { Clear: 8, PartlyCloudy: 12, Cloudy: 14, Drizzle: 12, Rain: 22, HeavyRain: 38, Thunderstorm: 55, Snow: 16, Fog: 3 };

function computeTargets() {
    let sc, wind = 8, windDir = 270, cloudPct = -1;
    const sim = cfg.weatherSim;
    if (sim && sim !== 'Auto' && SIM_CODES[sim] !== undefined) {
        sc = sceneFromCode(SIM_CODES[sim]);
        wind = SIM_WIND_KMH[sim] ?? 10;
    } else if (data && data.ok) {
        sc = sceneFromCode(data.code | 0);
        wind = +data.wind || 0; windDir = +data.windDir || 270;
        cloudPct = typeof data.cloud === 'number' ? data.cloud : -1;
        if (cloudPct >= 0) sc.cloud = Math.max(Math.min(1, cloudPct / 100 * 1.05), sc.rain > 0 || sc.snow > 0 ? 0.7 : 0);
    } else {
        sc = sceneFromCode(0);                                  // no data: clear sky, the time of day still works
    }
    // the user can force a wind strength (Beaufort preview) independently of the weather
    if (cfg.windSim && cfg.windSim !== 'Auto' && WIND_SIM_KMH[cfg.windSim] !== undefined) wind = WIND_SIM_KMH[cfg.windSim];
    TGT.cloud = sc.cloud; TGT.rain = sc.rain; TGT.snow = sc.snow; TGT.fog = sc.fog; TGT.storm = sc.storm;
    // wind in km/h -> px/s lateral drift for rain/snow (direction: the wind blows TOWARD windDir+180)
    const dirSign = -Math.sin(windDir * Math.PI / 180);
    const dir = Math.abs(dirSign) < 0.15 ? (dirSign < 0 ? -0.15 : 0.15) : dirSign;
    TGT.wind = clamp(wind, 0, 120);
    TGT.dir = dir < 0 ? -1 : 1;
    TGT.windX = clamp(wind, 0, 90) * 2.6 * dir;
}

// ---------------------------------------------------------------- generated assets
function buildCloudSprites() {
    cloudSprites = [];
    for (let v = 0; v < 5; v++) {
        const cw = 720, ch = 300, c = makeCanvas(cw, ch), g = c.getContext('2d');
        const rnd = mulberry32(1234 + v * 77);
        const base = [];
        const n = 34 + v * 4;
        for (let i = 0; i < n; i++) {
            const px = cw * (0.14 + 0.72 * rnd());
            const bell = Math.sin(clamp((px / cw - 0.1) / 0.8, 0, 1) * Math.PI);
            const py = ch * (0.72 - bell * 0.36 * rnd());
            base.push({ px, py, r: (30 + 50 * rnd()) * (0.55 + 0.75 * bell) });
        }
        const puff = (x, y, r, col, a0, a1, a2) => {
            const gr = g.createRadialGradient(x, y, 0, x, y, r);
            gr.addColorStop(0, `rgba(${col},${a0})`); gr.addColorStop(0.55, `rgba(${col},${a1})`); gr.addColorStop(0.9, `rgba(${col},${a2})`); gr.addColorStop(1, `rgba(${col},0)`);
            g.fillStyle = gr; g.beginPath(); g.arc(x, y, r, 0, TAU); g.fill();
        };
        // 1) shadowed body, 2) lit mass offset up/left, 3) small bright "cauliflower" detail on the top edge
        for (const q of base) puff(q.px, q.py, q.r, '132,142,162', 0.9, 0.7, 0.18);
        for (const q of base) puff(q.px - q.r * 0.16, q.py - q.r * 0.2, q.r * 0.86, '214,222,236', 0.85, 0.6, 0.12);
        for (const q of base) {
            for (let k = 0; k < 3; k++) {
                const ang = -Math.PI / 2 + (rnd() - 0.5) * 2.1, rr = q.r * (0.45 + rnd() * 0.35);
                puff(q.px + Math.cos(ang) * q.r * 0.55, q.py + Math.sin(ang) * q.r * 0.55 - q.r * 0.1, rr * 0.55, '255,255,255', 0.85, 0.5, 0.08);
            }
        }
        cloudSprites.push(c);
    }
    // flat, soft base so the cloud bottom is not a round blob
    for (const c of cloudSprites) {
        const g = c.getContext('2d');
        g.globalCompositeOperation = 'destination-out';
        const gr = g.createLinearGradient(0, c.height * 0.68, 0, c.height * 0.86);
        gr.addColorStop(0, 'rgba(0,0,0,0)'); gr.addColorStop(1, 'rgba(0,0,0,1)');
        g.fillStyle = gr; g.fillRect(0, c.height * 0.68, c.width, c.height * 0.32);
        g.globalCompositeOperation = 'source-over';
    }
}

function buildFlake() {
    flakeSprite = makeCanvas(32, 32);
    const g = flakeSprite.getContext('2d');
    const gr = g.createRadialGradient(16, 16, 0, 16, 16, 16);
    gr.addColorStop(0, 'rgba(255,255,255,1)'); gr.addColorStop(0.35, 'rgba(255,255,255,0.75)'); gr.addColorStop(1, 'rgba(255,255,255,0)');
    g.fillStyle = gr; g.fillRect(0, 0, 32, 32);
}

function buildFogSprites() {
    fogSprites = [];
    for (let v = 0; v < 3; v++) {
        const c = makeCanvas(1024, 256), g = c.getContext('2d'), rnd = mulberry32(900 + v * 31);
        for (let i = 0; i < 40; i++) {
            const x = rnd() * 1024, y = 100 + rnd() * 56, r = 40 + rnd() * 60;
            for (const off of [-1024, 0, 1024]) {                 // wrap horizontally so the sprite tiles without a seam
                const gr = g.createRadialGradient(x + off, y, 0, x + off, y, r * 1.7);
                gr.addColorStop(0, 'rgba(255,255,255,0.15)'); gr.addColorStop(1, 'rgba(255,255,255,0)');
                g.fillStyle = gr; g.save(); g.translate(x + off, y); g.scale(1, 0.4); g.translate(-(x + off), -y);
                g.beginPath(); g.arc(x + off, y, r * 1.7, 0, TAU); g.fill(); g.restore();
            }
        }
        fogSprites.push(c);
    }
}

function buildStars() {
    const rnd = mulberry32(42);
    stars = [];
    const n = 260;
    for (let i = 0; i < n; i++) {
        stars.push({ x: rnd(), y: Math.pow(rnd(), 1.25), r: rnd() < 0.08 ? 1.7 : 0.5 + rnd() * 0.9, ph: rnd() * TAU, sp: 0.6 + rnd() * 2.2, big: rnd() < 0.06 });
    }
}

function buildHills() {
    hills = [];
    const specs = [
        { depth: -0.35, amp: 0.2, base: -0.005, freq: [0.8, 2.1, 4.7], seed: 3 },
        { depth: 0.0, amp: 0.18, base: 0.0,  freq: [1.1, 2.7, 6.1], seed: 5 },
        { depth: 0.5, amp: 0.125, base: 0.01, freq: [1.7, 3.9, 8.3], seed: 11 },
        { depth: 1.0, amp: 0.07, base: 0.018, freq: [2.3, 5.3, 11.7], seed: 23 },
    ];
    const samples = 160;
    for (const s of specs) {
        const rnd = mulberry32(s.seed * 97);
        const ph = s.freq.map(() => rnd() * TAU);
        const pts = [];
        for (let i = 0; i <= samples; i++) {
            const x = i / samples;
            let h = 0;
            for (let k = 0; k < s.freq.length; k++) h += Math.sin(x * s.freq[k] * TAU * 0.5 + ph[k]) / (k + 1.2);
            h = (h + 1.4) / 2.8;                                // ~0..1
            // taper the middle so the sun/moon has an open horizon, keep silhouettes at the sides
            const edge = Math.pow(Math.abs(x - 0.5) * 2, 1.4);
            pts.push(s.base + s.amp * h * (0.3 + 0.7 * edge));
        }
        hills.push({ ...s, pts });
    }
}

function buildClouds() {
    clouds = [];
    const rnd = mulberry32(7);
    for (let i = 0; i < 30; i++) {
        const layer = i % 3;                                    // 0 high/small/slow, 2 low/big/fast
        clouds.push({
            sprite: i % cloudSprites.length,
            x: rnd(), y: 0.06 + rnd() * 0.5 + layer * 0.04,
            scale: 0.55 + layer * 0.35 + rnd() * 0.3,
            speed: 3 + layer * 5 + rnd() * 3,
            alpha: 0.55 + rnd() * 0.4,
            layer,
        });
    }
    clouds.sort((a, b) => a.layer - b.layer || a.y - b.y);
}

function buildDrops(density) {
    const area = (W * H) / (1920 * 1080);
    const make = (n, f) => Array.from({ length: Math.round(n * area * density) }, f);
    const rnd = Math.random;
    const groundSpan = () => HORIZON + rnd() * (H - HORIZON);
    rain.far = make(720, () => ({ x: rnd() * (W + 200), y: rnd() * H, v: 760 + rnd() * 160, l: 0.9, g: HORIZON + rnd() * (H - HORIZON) * 0.18, a: 0.5 + rnd() * 0.5 }));
    rain.mid = make(420, () => ({ x: rnd() * (W + 200), y: rnd() * H, v: 1150 + rnd() * 250, l: 1.2, g: HORIZON + (H - HORIZON) * (0.15 + rnd() * 0.35), a: 0.5 + rnd() * 0.5 }));
    rain.near = make(170, () => ({ x: rnd() * (W + 200), y: rnd() * H, v: 1750 + rnd() * 350, l: 1.7, g: HORIZON + (H - HORIZON) * (0.4 + rnd() * 0.6), a: 0.5 + rnd() * 0.5 }));
    void groundSpan;
}

function densityFor(profile) {
    switch (profile && profile.id) {
        case 'High': return 1.0;
        case 'Low Power': return 0.35;
        case 'Battery': return 0.15;
        default: return 0.65;
    }
}

// ---------------------------------------------------------------- public API
function resize(w, h, dpr) {
    W = Math.max(2, Math.round(w)); H = Math.max(2, Math.round(h)); DPR = dpr || 1;
    HORIZON = Math.round(H * 0.64);
    K = Math.max(0.7, W / 1920);
    skyCv = makeCanvas(W, HORIZON); skyCtx = skyCv.getContext('2d');
    cloudCv = makeCanvas(W / 2, HORIZON / 2); cloudCtx = cloudCv.getContext('2d');
    sceneCv = null; sceneCtx = null;
    lastDensity = -1;
    if (!cloudSprites.length) { buildCloudSprites(); buildFlake(); buildFogSprites(); buildStars(); buildClouds(); buildWaterAssets(); }
    reflCv = null;
    buildHills();
}

function setConfig(c) {
    const old = cfg;
    cfg = { ...cfg, ...c };
    if (typeof cfg.lat !== 'number' || isNaN(cfg.lat)) cfg.lat = 37.9838;
    if (typeof cfg.lon !== 'number' || isNaN(cfg.lon)) cfg.lon = 23.7275;
    if (old.weatherSim !== cfg.weatherSim || old.windSim !== cfg.windSim) computeTargets();
    sunCache.key = '';
}

function setWeather(d) { data = d; computeTargets(); }

// ---------------------------------------------------------------- drawing
function approach(cur, tgt, dt, rate) { return cur + (tgt - cur) * Math.min(1, dt * rate); }

function drawSkyLayer(sky, t, sr, ss) {
    const g = skyCtx;
    g.globalCompositeOperation = 'source-over';
    g.globalAlpha = 1;
    // weather grading: overcast skies are grey and darker, storms darker still
    const grey = clamp(S.cloud * 0.8 + S.storm * 0.15, 0, 0.92);
    const dark = clamp(S.cloud * 0.22 + S.storm * 0.34 + S.rain * 0.1 + S.snow * 0.05, 0, 0.7);
    const grade = (c) => { const L = lum(c); return scaleCol(mix(c, [L, L, L * 1.02], grey), 1 - dark); };
    const top = grade(sky.top), mid = grade(sky.mid), hor = grade(sky.hor);
    const hz = HORIZON;
    const gr = g.createLinearGradient(0, 0, 0, hz);
    gr.addColorStop(0, rgba(top, 1)); gr.addColorStop(0.55, rgba(mid, 1)); gr.addColorStop(1, rgba(hor, 1));
    g.fillStyle = gr; g.fillRect(0, 0, W, hz);
    return { top, mid, hor };
}

function drawStarsLayer(sky, vis) {
    const a = vis * (1 - sky.amb * 2.4);
    if (a <= 0.02) return;
    const g = skyCtx;
    for (const s of stars) {
        const tw = 0.6 + 0.4 * Math.sin(animTime * s.sp + s.ph);
        const x = s.x * W, y = s.y * HORIZON * 0.95;
        g.fillStyle = `rgba(235,242,255,${clamp(a * tw, 0, 1)})`;
        g.beginPath(); g.arc(x, y, s.r, 0, TAU); g.fill();
        if (s.big && a * tw > 0.4) {
            g.strokeStyle = `rgba(235,242,255,${a * tw * 0.4})`; g.lineWidth = 0.7;
            g.beginPath(); g.moveTo(x - 5, y); g.lineTo(x + 5, y); g.moveTo(x, y - 5); g.lineTo(x, y + 5); g.stroke();
        }
    }
    // shooting star on clear nights
    if (shoot) {
        const p = shoot.life / shoot.dur, hx = shoot.x + shoot.dx * p, hy = shoot.y + shoot.dy * p;
        const tx = hx - shoot.dx * 0.18, ty = hy - shoot.dy * 0.18;
        const lg = g.createLinearGradient(tx, ty, hx, hy);
        lg.addColorStop(0, 'rgba(255,255,255,0)'); lg.addColorStop(1, `rgba(255,255,255,${0.9 * (1 - p) * a})`);
        g.strokeStyle = lg; g.lineWidth = 1.6; g.beginPath(); g.moveTo(tx, ty); g.lineTo(hx, hy); g.stroke();
    }
}

function drawMoonLayer(t, sr, ss, vis) {
    if (vis <= 0.02) return null;
    const g = skyCtx;
    const night = 1440 - (ss - sr);
    let v = ((t - ss) % 1440 + 1440) % 1440 / night;
    if (v > 1) return null;
    const e = Math.sin(Math.PI * clamp(v, 0, 1));
    const x = W * (0.12 + 0.76 * v), y = HORIZON - e * HORIZON * 0.72 + 6;
    const r = Math.max(16, Math.min(W, H) * 0.034);
    const alpha = vis * (1 - S.cloud * 0.9) * smooth((e + 0.06) / 0.14);
    if (alpha <= 0.02) return { x, y, r, alpha: 0 };
    // halo
    g.globalCompositeOperation = 'lighter';
    const halo = g.createRadialGradient(x, y, r * 0.8, x, y, r * 7);
    halo.addColorStop(0, `rgba(190,205,240,${0.30 * alpha})`); halo.addColorStop(0.12, `rgba(190,205,240,${0.16 * alpha})`);
    halo.addColorStop(0.35, `rgba(190,205,240,${0.06 * alpha})`); halo.addColorStop(0.7, `rgba(190,205,240,${0.015 * alpha})`); halo.addColorStop(1, 'rgba(190,205,240,0)');
    g.fillStyle = halo; g.beginPath(); g.arc(x, y, r * 7, 0, TAU); g.fill();
    g.globalCompositeOperation = 'source-over';
    // disc: earthshine + lit part with the real phase
    g.save(); g.translate(x, y);
    g.globalAlpha = alpha;
    g.fillStyle = 'rgba(60,70,95,0.9)'; g.beginPath(); g.arc(0, 0, r, 0, TAU); g.fill();
    const p = moonPhase();
    const waning = p > 0.5, ph = waning ? (1 - p) * TAU : p * TAU;     // 0..PI
    const c = Math.cos(ph);
    g.save(); if (waning) g.scale(-1, 1);
    g.beginPath();
    const N = 36;
    for (let i = 0; i <= N; i++) { const th = -Math.PI / 2 + Math.PI * i / N; const px = r * Math.cos(th), py = r * Math.sin(th); i ? g.lineTo(px, py) : g.moveTo(px, py); }
    for (let i = N; i >= 0; i--) { const th = -Math.PI / 2 + Math.PI * i / N; g.lineTo(r * c * Math.cos(th), r * Math.sin(th)); }
    g.closePath();
    const lg = g.createRadialGradient(-r * 0.25, -r * 0.25, r * 0.1, 0, 0, r);
    lg.addColorStop(0, 'rgba(255,252,240,1)'); lg.addColorStop(1, 'rgba(214,220,232,1)');
    g.fillStyle = lg; g.fill();
    g.clip();
    g.fillStyle = 'rgba(120,130,150,0.28)';
    for (const [cx, cy, cr] of [[-0.3, -0.2, 0.22], [0.25, 0.1, 0.3], [-0.05, 0.45, 0.18], [0.35, -0.4, 0.14]]) { g.beginPath(); g.arc(cx * r, cy * r, cr * r, 0, TAU); g.fill(); }
    g.restore();
    g.restore();
    return { x, y, r, alpha };
}

function drawSunLayer(sky, t, sr, ss) {
    const u = (t - sr) / Math.max(1, ss - sr);
    if (u < -0.12 || u > 1.12) return null;
    const e = Math.sin(Math.PI * clamp(u, -0.12, 1.12));
    const x = W * (0.1 + 0.8 * clamp(u, -0.1, 1.1)), y = HORIZON - e * HORIZON * 0.78;
    const r = Math.max(18, Math.min(W, H) * 0.04);
    const vis = smooth((e + 0.1) / 0.16) * (1 - S.cloud * 0.82) * (1 - S.fog * 0.3);
    const g = skyCtx;
    // wide horizon glow follows the sun even behind clouds (dimmer)
    const warm = mix(hex('#ffffff'), hex('#ff9a5a'), clamp(1 - e * 2.2, 0, 1));
    g.globalCompositeOperation = 'lighter';
    const glowA = (0.18 + 0.5 * sky.glow) * (0.35 + 0.65 * (1 - S.cloud * 0.7));
    const gg = g.createRadialGradient(x, y, 0, x, y, Math.max(W, HORIZON) * 0.55);
    gg.addColorStop(0, rgba(warm, glowA * 0.75)); gg.addColorStop(0.25, rgba(warm, glowA * 0.28)); gg.addColorStop(1, rgba(warm, 0));
    g.fillStyle = gg; g.fillRect(0, 0, W, HORIZON);
    if (vis > 0.02) {
        const cg = g.createRadialGradient(x, y, r * 0.2, x, y, r * 3.2);
        cg.addColorStop(0, rgba(warm, 0.95 * vis)); cg.addColorStop(0.3, rgba(warm, 0.35 * vis)); cg.addColorStop(1, rgba(warm, 0));
        g.fillStyle = cg; g.beginPath(); g.arc(x, y, r * 3.2, 0, TAU); g.fill();
        g.fillStyle = rgba(mix(warm, [255, 255, 255], 0.6), vis);
        g.beginPath(); g.arc(x, y, r, 0, TAU); g.fill();
    }
    g.globalCompositeOperation = 'source-over';
    return { x, y, r, vis, e };
}

function cloudTint(sky) {
    const dayCol = [255, 255, 255], nightCol = [34, 42, 68];
    let c = mix(nightCol, dayCol, clamp(sky.amb * 1.15, 0, 1));
    c = mix(c, scaleCol(sky.hor, 1.05), clamp(sky.glow * 0.55, 0, 0.6));
    const heavy = clamp(S.storm * 0.95 + S.rain * 0.55 + S.cloud * 0.18, 0, 0.92);
    c = mix(c, scaleCol([86, 92, 106], 0.25 + 0.75 * clamp(sky.amb * 1.2, 0, 1)), heavy);
    c = scaleCol(c, 1 - flash * 0.0);
    if (flash > 0) c = mix(c, [235, 240, 255], clamp(flash, 0, 1));
    return c;
}

function drawCloudsLayer(sky, dt) {
    // rain/storm make the sky denser and lower: effective cover grows, clouds get bigger and sit lower
    const cover = clamp(S.cloud + S.rain * 0.35 + S.storm * 0.2, 0, 1);
    if (cover < 0.04) return;
    const g = cloudCtx, cw = cloudCv.width, ch = cloudCv.height;
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.globalCompositeOperation = 'source-over';
    g.clearRect(0, 0, cw, ch);
    const count = Math.min(clouds.length, Math.round(2 + 16 * cover + 8 * S.rain + 4 * S.storm));
    // clouds travel with the wind: ~3 px/s in calm air up to >100 px/s in a gale (per layer parallax: low clouds move faster)
    const base = (3 + S.wind * 1.5) * K;
    const dir = S.dir < 0 ? -1 : 1;
    const lower = 0.07 * (S.rain + S.storm * 0.5);
    const grow = 1 + 0.3 * S.rain + 0.15 * S.storm;
    for (let i = 0; i < clouds.length; i++) {
        const c = clouds[i];
        c.x += (dir * base * (0.45 + 0.4 * c.layer) * (0.85 + (c.speed % 3) * 0.08) * dt) / W;
        if (c.x > 1.3) c.x -= 1.6; else if (c.x < -0.3) c.x += 1.6;
        if (i >= count) continue;
        const sp = cloudSprites[c.sprite];
        const w = sp.width * c.scale * grow * (W / 1920) * 0.5, h = sp.height * c.scale * grow * (W / 1920) * 0.5;
        g.globalAlpha = clamp(c.alpha * (0.35 + cover * 0.75) * (1 - S.fog * 0.45) + S.rain * 0.12, 0, 1);
        g.drawImage(sp, c.x * cw - w / 2, (c.y + lower) * ch - h / 2, w, h);
    }
    g.globalAlpha = 1;
    g.globalCompositeOperation = 'source-atop';
    const tint = cloudTint(sky);
    g.fillStyle = rgba(tint, 0.62 + 0.2 * clamp(S.storm + S.rain * 0.5, 0, 1));
    g.fillRect(0, 0, cw, ch);
    g.globalCompositeOperation = 'source-over';
    skyCtx.globalAlpha = 1;
    skyCtx.drawImage(cloudCv, 0, 0, W, HORIZON);
    // heavy rain: a dark, dense overcast deck across the upper sky
    const deck = clamp(S.rain * 0.7 + S.storm * 0.35 - 0.1, 0, 0.75);
    if (deck > 0.02) {
        const dg = skyCtx.createLinearGradient(0, 0, 0, HORIZON * 0.62);
        const dc = scaleCol([70, 78, 92], 0.3 + 0.7 * clamp(sky.amb * 1.2, 0, 1));
        dg.addColorStop(0, rgba(dc, deck)); dg.addColorStop(1, rgba(dc, 0));
        skyCtx.fillStyle = dg; skyCtx.fillRect(0, 0, W, HORIZON * 0.62);
    }
}

function hazeColor(hor) { return mix(hor, [190, 196, 206], 0.4); }

function drawHillsLayer(sky, graded) {
    const g = skyCtx;
    const amb = clamp(sky.amb, 0, 1);
    const shade = scaleCol([22, 32, 46], 0.28 + 0.72 * amb);
    const haze = hazeColor(graded.hor);
    for (const h of hills) {
        let col = mix(graded.hor, shade, 0.22 + 0.62 * h.depth * h.depth + 0.12 * h.depth);
        col = mix(col, haze, clamp(S.fog * (0.9 - 0.35 * h.depth), 0, 0.9));
        col = mix(col, [212, 222, 238].map(v => v * (0.3 + 0.7 * amb)), clamp(S.snowCover * 0.75, 0, 0.8));
        if (flash > 0) col = mix(col, [200, 210, 235], clamp(flash * 0.5, 0, 0.6));
        g.fillStyle = rgba(col, 1);
        g.beginPath();
        g.moveTo(0, HORIZON + 2);
        const n = h.pts.length - 1;
        for (let i = 0; i <= n; i++) g.lineTo((i / n) * W, HORIZON - h.pts[i] * H);
        g.lineTo(W, HORIZON + 2); g.closePath(); g.fill();
    }
}

function drawLightning(dt, sky) {
    if (S.storm < 0.3) { bolt = null; flash = 0; return; }
    nextBoltIn -= dt;
    if (nextBoltIn <= 0 && !bolt) {
        nextBoltIn = 3.5 + Math.random() * 9;
        const x0 = W * (0.15 + Math.random() * 0.7);
        const pts = [[x0, HORIZON * 0.12]];
        let x = x0, y = HORIZON * 0.12;
        const segs = 14;
        for (let i = 0; i < segs; i++) {
            y += (HORIZON * 0.9 - HORIZON * 0.12) / segs * (0.7 + Math.random() * 0.6);
            x += (Math.random() - 0.5) * 70 + S.windX * 0.05;
            pts.push([x, Math.min(y, HORIZON)]);
        }
        const branches = [];
        for (let i = 3; i < pts.length - 2; i += 2) if (Math.random() < 0.55) {
            let bx = pts[i][0], by = pts[i][1]; const b = [[bx, by]];
            const dir = Math.random() < 0.5 ? -1 : 1;
            for (let k = 0; k < 4; k++) { bx += dir * (14 + Math.random() * 26); by += 16 + Math.random() * 28; b.push([bx, by]); }
            branches.push(b);
        }
        bolt = { pts, branches, life: 0, dur: 0.55 };
    }
    if (bolt) {
        bolt.life += dt;
        const p = bolt.life / bolt.dur;
        // double flicker: strong, dip, stronger, fade
        const f = p < 0.12 ? p / 0.12 : p < 0.25 ? 0.35 : p < 0.38 ? 1 : Math.max(0, 1 - (p - 0.38) / 0.62);
        flash = clamp(f * 0.55 * S.storm, 0, 0.6);
        if (p >= 1) { bolt = null; flash = 0; }
    } else flash = Math.max(0, flash - dt * 2);
}

function drawBolt(ctx2) {
    if (!bolt) return;
    const p = bolt.life / bolt.dur;
    const a = p < 0.5 ? 1 : Math.max(0, 1 - (p - 0.5) / 0.5);
    ctx2.save();
    ctx2.globalCompositeOperation = 'lighter';
    ctx2.lineCap = 'round'; ctx2.lineJoin = 'round';
    const stroke = (pts, w, col) => { ctx2.strokeStyle = col; ctx2.lineWidth = w; ctx2.beginPath(); pts.forEach((q, i) => i ? ctx2.lineTo(q[0], q[1]) : ctx2.moveTo(q[0], q[1])); ctx2.stroke(); };
    ctx2.shadowColor = 'rgba(170,190,255,0.9)'; ctx2.shadowBlur = 24;
    stroke(bolt.pts, 5, `rgba(150,170,255,${0.45 * a})`);
    for (const b of bolt.branches) stroke(b, 3, `rgba(150,170,255,${0.3 * a})`);
    ctx2.shadowBlur = 8;
    stroke(bolt.pts, 2, `rgba(255,255,255,${0.95 * a})`);
    for (const b of bolt.branches) stroke(b, 1.1, `rgba(255,255,255,${0.8 * a})`);
    ctx2.restore();
}

// Position/strength of the sun or moon (set by draw()), used for the shimmering light path on the water
let celest = null;
let reflCv = null, reflCtx = null;
let glitter = [], glints = [], caps = [];
function buildWaterAssets() {
    const rnd = mulberry32(2024);
    glitter = Array.from({ length: 150 }, () => ({ u: rnd(), side: rnd() - 0.5, len: 0.4 + rnd() * 0.9, sp: 1.2 + rnd() * 3.2, ph: rnd() * TAU, hh: 0.7 + rnd() * 0.8 }));
    caps = Array.from({ length: 170 }, () => ({ u: rnd(), x: rnd(), len: 0.5 + rnd() * 1.2, sp: 1 + rnd() * 3, ph: rnd() * TAU }));
    glints = Array.from({ length: 70 }, () => ({ u: rnd(), x: rnd(), len: 0.04 + rnd() * 0.12, sp: 0.4 + rnd() * 1.4, a: 0.35 + rnd() * 0.65 }));
}

function drawWater(ctx2, sky, graded) {
    const hz = HORIZON, wh = H - hz;
    const amb = clamp(sky.amb, 0, 1);
    // deep water colour
    const deep = mix(scaleCol(graded.top, 0.5), [3, 7, 15], 0.4);
    const shore = mix(graded.hor, deep, 0.5);
    const wg = ctx2.createLinearGradient(0, hz, 0, H);
    wg.addColorStop(0, rgba(shore, 1)); wg.addColorStop(0.45, rgba(mix(shore, deep, 0.6), 1)); wg.addColorStop(1, rgba(deep, 1));
    ctx2.fillStyle = wg; ctx2.fillRect(0, hz, W, wh);

    // sea state follows the wind (Beaufort): 0-2 mirror-like, 3-4 gentle ripples, 5-6 choppy with whitecaps, 7+ rough
    const bfk = clamp(BFN / 10, 0, 1);
    const calm = 1 - clamp(bfk * 0.85 + S.rain * 0.25, 0, 0.92);
    const refl = (0.7 - 0.22 * S.cloud) * (1 - S.snowCover) * (1 - 0.6 * bfk);
    if (refl > 0.03) {
        // 1) the sky is mirrored into a SMALL canvas (1/3 size): the down-scaling gives a naturally soft reflection
        const rw = Math.max(2, Math.round(W / 3)), rh = Math.max(2, Math.round(wh / 3));
        if (!reflCv || reflCv.width !== rw || reflCv.height !== rh) { reflCv = makeCanvas(rw, rh); reflCtx = reflCv.getContext('2d'); }
        const srcH = Math.min(hz, wh * 1.25);
        reflCtx.globalCompositeOperation = 'source-over';
        reflCtx.clearRect(0, 0, rw, rh);
        reflCtx.save(); reflCtx.translate(0, rh); reflCtx.scale(1, -1);
        reflCtx.drawImage(skyCv, 0, hz - srcH, W, srcH, 0, 0, rw, rh);
        reflCtx.restore();
        // 2) erase the mirrored sun/moon DISC (a perfect mirrored disc is what made the old reflection look cartoonish) — the
        //    glitter path below replaces it
        if (celest && celest.power > 0.02) {
            const cx = celest.x / 3, cy = ((hz - celest.y) / 1.25) / 3, rr = Math.max(8, celest.r * 3.4 / 3);
            reflCtx.globalCompositeOperation = 'destination-out';
            const eg = reflCtx.createRadialGradient(cx, cy, 0, cx, cy, rr);
            eg.addColorStop(0, 'rgba(0,0,0,0.95)'); eg.addColorStop(0.6, 'rgba(0,0,0,0.6)'); eg.addColorStop(1, 'rgba(0,0,0,0)');
            reflCtx.fillStyle = eg; reflCtx.beginPath(); reflCtx.arc(cx, cy, rr, 0, TAU); reflCtx.fill();
            reflCtx.globalCompositeOperation = 'source-over';
        }
        // 3) paint it into the water in thin bands with smooth, depth-dependent wobble (ripples) and a fade with distance
        ctx2.save();
        ctx2.beginPath(); ctx2.rect(0, hz, W, wh); ctx2.clip();
        ctx2.imageSmoothingEnabled = true; ctx2.imageSmoothingQuality = 'high';
        const band = Math.max(3, Math.round(H / 220));
        const amp = (0.45 + BFN * 0.5 + S.rain * 1.4) * K;
        const fw = 0.8 + BFN * 0.16;                      // ripple speed grows with the wind
        for (let y = 0; y < wh; y += band) {
            const k = y / wh;
            const dx = (Math.sin(animTime * 0.9 * fw + y * (0.05 + bfk * 0.05)) * amp + Math.sin(animTime * 1.9 * fw + y * 0.13) * amp * 0.45) * (0.35 + k);
            ctx2.globalAlpha = refl * (1 - k * 0.78);
            // sample slightly more than one source row (and centre it) so neighbouring bands blend: no blocky steps on hill edges
            const sy = Math.max(0, y / 3 - 0.5), sh = Math.min(rh - sy, band / 3 + 1);
            ctx2.drawImage(reflCv, 0, sy, rw, sh, dx - amp * 2, hz + y, W + amp * 4, band);
        }
        ctx2.restore();
    }

    // snow-covered ground replaces the water
    if (S.snowCover > 0.02) {
        const sg = ctx2.createLinearGradient(0, hz, 0, H);
        const base = scaleCol([214, 224, 240], 0.28 + 0.72 * amb);
        sg.addColorStop(0, rgba(mix(base, graded.hor, 0.3), S.snowCover));
        sg.addColorStop(1, rgba(scaleCol(base, 0.85), S.snowCover));
        ctx2.fillStyle = sg; ctx2.fillRect(0, hz, W, wh);
    }

    // long, faint wave glints drifting with the wind (surface texture)
    if (S.snowCover < 0.5) {
        ctx2.save();
        ctx2.globalCompositeOperation = 'lighter';
        const gcol = mix(graded.hor, [235, 240, 250], 0.55);
        const activeGlints = Math.round(glints.length * clamp(0.3 + BFN / 7, 0, 1));
        for (let gi = 0; gi < activeGlints; gi++) {
            const g = glints[gi];
            const t = Math.pow(g.u, 1.5);
            const y = hz + 6 + t * wh * 0.96;
            const len = (g.len * (0.35 + t * 1.6)) * W;
            const x = (((g.x * W + animTime * g.sp * (6 + t * 22) * (0.5 + S.wind * 0.022) * (S.dir < 0 ? -1 : 1)) % (W + len)) + W + len) % (W + len) - len;
            ctx2.fillStyle = rgba(gcol, 0.05 * g.a * (0.4 + t) * (0.4 + 0.6 * amb) * (0.65 + BFN * 0.12));
            ctx2.fillRect(x, y, len, Math.max(1, (1 + t * 2.2) * K));
        }
        ctx2.restore();
    }

    // whitecaps: short foamy dashes on a choppy sea (Beaufort 4+), drifting with the wind
    const capK = clamp((BFN - 3.5) / 4, 0, 1);
    if (capK > 0.02 && S.snowCover < 0.5) {
        ctx2.save();
        ctx2.globalCompositeOperation = 'lighter';
        const n = Math.round(caps.length * capK);
        const ccol = mix([200, 210, 225], [255, 255, 255], amb);
        for (let ci = 0; ci < n; ci++) {
            const c = caps[ci];
            const t = Math.pow(c.u, 1.25);
            const y = hz + 10 + t * wh * 0.94;
            const len = (6 + 30 * t) * c.len * K;
            const x = (((c.x * W + animTime * (14 + BFN * 6) * (0.4 + t) * (S.dir < 0 ? -1 : 1)) % (W + len)) + W + len) % (W + len) - len;
            const tw = 0.4 + 0.6 * Math.abs(Math.sin(animTime * c.sp + c.ph));
            ctx2.fillStyle = rgba(ccol, 0.32 * capK * tw * (0.3 + 0.7 * t) * (0.3 + 0.7 * amb));
            ctx2.fillRect(x, y, len, Math.max(1, (0.8 + 1.8 * t) * K));
        }
        ctx2.restore();
    }

    // shimmering sun/moon light path (glitter): narrow near the horizon, wider and brighter-flecked toward the viewer
    if (celest && celest.power > 0.02 && S.snowCover < 0.6) {
        ctx2.save();
        ctx2.beginPath(); ctx2.rect(0, hz, W, wh); ctx2.clip();
        ctx2.globalCompositeOperation = 'lighter';
        const p = celest.power * (0.55 + 0.45 * calm);
        // soft column of light
        const colH = wh * 0.95, colW = W * 0.07;
        ctx2.save();
        ctx2.translate(celest.x, hz + colH * 0.42); ctx2.scale(colW / colH, 1);
        const cg = ctx2.createRadialGradient(0, 0, 0, 0, 0, colH * 0.55);
        cg.addColorStop(0, rgba(celest.col, 0.20 * p)); cg.addColorStop(0.5, rgba(celest.col, 0.07 * p)); cg.addColorStop(1, rgba(celest.col, 0));
        ctx2.fillStyle = cg; ctx2.beginPath(); ctx2.arc(0, 0, colH * 0.55, 0, TAU); ctx2.fill();
        ctx2.restore();
        // glitter flecks
        for (const f of glitter) {
            const t = Math.pow(f.u, 1.35);
            const y = hz + 4 + t * wh * 0.97;
            const spread = (0.012 + 0.11 * t) * W * (1 + BFN * 0.12);
            const x = celest.x + f.side * 2 * spread + Math.sin(animTime * 0.7 + f.ph) * spread * 0.18;
            const len = (4 + 46 * t) * f.len * K * (1 - bfk * 0.45);
            const tw = 0.5 + 0.5 * Math.sin(animTime * f.sp * (0.8 + BFN * 0.15) + f.ph);
            const a = p * (1 - t * 0.5) * (0.12 + 0.88 * tw * tw) * 0.75 * (1 - Math.abs(f.side) * 0.9);
            if (a < 0.015) continue;
            ctx2.fillStyle = rgba(celest.col, a);
            ctx2.fillRect(x - len / 2, y, len, Math.max(1, (0.9 + 2.4 * t) * K * f.hh));
        }
        ctx2.restore();
    }

    // soft haze right at the horizon
    const hg = ctx2.createLinearGradient(0, hz - 6, 0, hz + wh * 0.14);
    hg.addColorStop(0, rgba(graded.hor, 0)); hg.addColorStop(0.3, rgba(graded.hor, 0.4)); hg.addColorStop(1, rgba(graded.hor, 0));
    ctx2.fillStyle = hg; ctx2.fillRect(0, hz - 6, W, wh * 0.14 + 6);
}

function drawRipples(ctx2, dt, amb) {
    if (S.snowCover > 0.6) { ripples.length = 0; return; }
    const col = mix([120, 140, 170], [225, 235, 250], clamp(amb + 0.15, 0, 1));
    ctx2.lineWidth = 1;
    for (let i = ripples.length - 1; i >= 0; i--) {
        const r = ripples[i];
        r.life += dt;
        const p = r.life / r.dur;
        if (p >= 1) { ripples.splice(i, 1); continue; }
        const rx = r.maxR * (0.15 + p), ry = rx * (0.16 + 0.1 * r.k);
        ctx2.strokeStyle = rgba(col, (1 - p) * (0.5 + 0.3 * r.k));
        ctx2.beginPath(); ctx2.ellipse(r.x, r.y, rx, ry, 0, 0, TAU); ctx2.stroke();
        if (r.k > 0.5 && p < 0.7) { ctx2.beginPath(); ctx2.ellipse(r.x, r.y, rx * 0.55, ry * 0.55, 0, 0, TAU); ctx2.stroke(); }
    }
}

function drawRain(ctx2, dt, amb, density) {
    const intensity = S.rain;
    if (intensity < 0.02) return;
    const col = mix([135, 152, 180], [215, 230, 250], clamp(amb + 0.1, 0, 1));
    const layers = [
        { arr: rain.far, w: 0.8, a: 0.24, len: 0.045, rip: 0.22, k: 0.15 },
        { arr: rain.mid, w: 1.15, a: 0.34, len: 0.05, rip: 0.4, k: 0.5 },
        { arr: rain.near, w: 1.7, a: 0.42, len: 0.055, rip: 0.7, k: 1 },
    ];
    ctx2.lineCap = 'round';
    const wind = S.windX;
    for (const L of layers) {
        const n = Math.round(L.arr.length * Math.min(1, intensity * 1.15));
        ctx2.strokeStyle = rgba(col, L.a * (0.55 + 0.45 * intensity));
        ctx2.lineWidth = L.w * Math.sqrt(K);
        ctx2.beginPath();
        for (let i = 0; i < n; i++) {
            const d = L.arr[i];
            d.y += d.v * K * dt; d.x += wind * K * (d.v / 1200) * dt;
            if (d.y >= d.g) {
                if (Math.random() < L.rip * (0.5 + 0.5 * intensity) && ripples.length < 260 && S.snowCover < 0.5)
                    ripples.push({ x: d.x, y: d.g, life: 0, dur: 0.7 + Math.random() * 0.6, maxR: (7 + L.k * 24 + Math.random() * 8) * K, k: L.k });
                if (L.k > 0.45 && density >= 0.6 && splashes.length < 320 && Math.random() < 0.6 && S.snowCover < 0.5) {
                    for (let s = 0; s < 3; s++) splashes.push({ x: d.x, y: d.g, vx: (Math.random() - 0.5) * 90, vy: -(60 + Math.random() * 120), life: 0, dur: 0.35 + Math.random() * 0.2 });
                }
                d.y = -Math.random() * 120 - d.v * 0.05;
                d.x = Math.random() * (W + 240) - 120;
                d.g = d.g;
            }
            if (d.x > W + 120) d.x -= W + 240; else if (d.x < -120) d.x += W + 240;
            const k = L.len;
            ctx2.moveTo(d.x, d.y);
            ctx2.lineTo(d.x - wind * K * (d.v / 1200) * k, d.y - d.v * K * k);
        }
        ctx2.stroke();
    }
    // splash droplets
    if (splashes.length) {
        ctx2.fillStyle = rgba(col, 0.55);
        for (let i = splashes.length - 1; i >= 0; i--) {
            const s = splashes[i];
            s.life += dt;
            if (s.life >= s.dur) { splashes.splice(i, 1); continue; }
            s.vy += 650 * dt; s.x += s.vx * dt; s.y += s.vy * dt;
            ctx2.globalAlpha = 1 - s.life / s.dur;
            ctx2.fillRect(s.x, s.y, 1.6, 1.6);
        }
        ctx2.globalAlpha = 1;
    }
}

const flakes = { arr: [], n: 0 };
function ensureFlakes(density) {
    const want = Math.round(520 * (W * H) / (1920 * 1080) * density);
    if (flakes.n === want) return;
    flakes.n = want;
    flakes.arr = Array.from({ length: want }, () => {
        const z = Math.random();                                // 0 far .. 1 near
        return { x: Math.random() * W, y: Math.random() * H, z, v: 28 + z * 80, r: 1.2 + z * 4.2, ph: Math.random() * TAU, sw: 12 + z * 26 };
    });
}
function drawSnow(ctx2, dt, amb) {
    if (S.snow < 0.02) return;
    const n = Math.round(flakes.arr.length * Math.min(1, S.snow * 1.1));
    const bright = 0.45 + 0.55 * clamp(amb + 0.25, 0, 1);
    for (let i = 0; i < n; i++) {
        const f = flakes.arr[i];
        f.y += f.v * K * dt;
        f.x += (S.windX * 0.35 * (0.4 + f.z) + Math.sin(animTime * 0.9 + f.ph) * f.sw) * dt;
        if (f.y > H + 10) { f.y = -10; f.x = Math.random() * W; }
        if (f.x > W + 10) f.x -= W + 20; else if (f.x < -10) f.x += W + 20;
        ctx2.globalAlpha = (0.35 + 0.55 * f.z) * bright;
        const s = f.r * 2 * K;
        ctx2.drawImage(flakeSprite, f.x - s, f.y - s, s * 2, s * 2);
    }
    ctx2.globalAlpha = 1;
}

function drawFog(ctx2, dt, graded, amb) {
    if (S.fog < 0.03) return;
    const col = mix(hazeColor(graded.hor), [235, 238, 244], 0.25);
    const base = mix([30, 36, 50], col, clamp(amb * 1.1 + 0.15, 0, 1));
    // veil over the whole scene
    ctx2.fillStyle = rgba(base, S.fog * 0.34);
    ctx2.fillRect(0, 0, W, H);
    // drifting banks (each sprite tiles seamlessly, so a bank is a full-width band without visible edges)
    ctx2.save();
    for (let i = 0; i < 6; i++) {
        const sp = fogSprites[i % 3];
        const bw = W * (0.8 + 0.15 * (i % 3)), bh = bw * 0.25;
        const spd = 6 + i * 4 + S.windX * 0.05;
        const off = ((animTime * spd + i * 413) % bw + bw) % bw;
        const y = HORIZON - bh * 0.55 + (i - 2.5) * H * 0.045;
        ctx2.globalAlpha = clamp(S.fog * (0.5 + 0.3 * Math.sin(i + animTime * 0.1)), 0, 1) * (0.5 + 0.5 * clamp(amb + 0.2, 0, 1));
        for (let x = off - bw - 1; x < W; x += bw) ctx2.drawImage(sp, x, y, bw + 2, bh);
    }
    ctx2.restore();
}

// ---- rain on the "window glass"
function updateGlass(dt) {
    const want = cfg.glass && S.rain > 0.12 ? Math.round((24 + 110 * S.rain) * Math.min(1.4, W / 1920 + 0.2)) : 0;
    while (glass.length < want && Math.random() < 0.6) {
        glass.push({ x: Math.random() * W, y: Math.random() * H, r: (1.6 + Math.random() * 4.5) * K, vy: 0, trail: [], age: 0 });
    }
    for (let i = glass.length - 1; i >= 0; i--) {
        const d = glass[i];
        d.age += dt;
        if (want === 0 || glass.length > want + 6) { d.r -= dt * 2; if (d.r < 0.5) { glass.splice(i, 1); continue; } }
        d.r = Math.min(10, d.r + dt * 0.045 * S.rain * (d.vy > 1 ? 0 : 1));
        if (d.vy === 0 && d.r > 5 + Math.random() * 3) d.vy = 12 + Math.random() * 14;
        if (d.vy > 0) {
            d.vy = Math.min(220, d.vy * (1 + dt * 1.3));
            d.y += d.vy * dt;
            d.x += Math.sin(d.y * 0.04 + i) * dt * 4;
            d.trail.push([d.x, d.y]); if (d.trail.length > 28) d.trail.shift();
            d.r -= dt * 0.55;
            if (d.r < 1.4 || d.y > H + 20) { d.vy = 0; d.y = Math.random() * H * 0.9; d.x = Math.random() * W; d.r = 1.4 + Math.random() * 2; d.trail.length = 0; }
        }
    }
}

function drawGlass(ctx2, src) {
    if (!glass.length) return;
    ctx2.save();
    for (const d of glass) {
        if (d.trail.length > 1) {
            ctx2.strokeStyle = 'rgba(255,255,255,0.07)'; ctx2.lineWidth = Math.max(1, d.r * 0.5); ctx2.lineCap = 'round';
            ctx2.beginPath(); d.trail.forEach((q, i) => i ? ctx2.lineTo(q[0], q[1]) : ctx2.moveTo(q[0], q[1])); ctx2.stroke();
        }
        const r = d.r, x = d.x, y = d.y;
        // refraction: the drop shows a magnified, upside-down piece of the scene behind it
        if (src && r > 2.2) {
            ctx2.save();
            ctx2.beginPath(); ctx2.ellipse(x, y, r, r * 1.18, 0, 0, TAU); ctx2.clip();
            const m = 2.4, sw = r * 2 * m, sh = r * 2.36 * m;
            ctx2.translate(x, y); ctx2.rotate(Math.PI);
            ctx2.drawImage(src, clamp(x - sw / 2, 0, W - sw), clamp(y - sh / 2, 0, H - sh), sw, sh, -r, -r * 1.18, r * 2, r * 2.36);
            ctx2.restore();
        }
        // rim + specular
        ctx2.beginPath(); ctx2.ellipse(x, y, r, r * 1.18, 0, 0, TAU);
        const rg = ctx2.createRadialGradient(x - r * 0.25, y - r * 0.3, r * 0.1, x, y, r * 1.2);
        rg.addColorStop(0, 'rgba(255,255,255,0.05)'); rg.addColorStop(0.75, 'rgba(255,255,255,0.1)'); rg.addColorStop(1, 'rgba(10,16,30,0.35)');
        ctx2.fillStyle = rg; ctx2.fill();
        ctx2.strokeStyle = 'rgba(255,255,255,0.22)'; ctx2.lineWidth = 0.8; ctx2.stroke();
        ctx2.fillStyle = 'rgba(255,255,255,0.75)';
        ctx2.beginPath(); ctx2.arc(x - r * 0.35, y - r * 0.45, Math.max(0.6, r * 0.2), 0, TAU); ctx2.fill();
    }
    ctx2.restore();
}

function drawVignette(ctx2) {
    const g = ctx2.createRadialGradient(W / 2, H / 2, Math.min(W, H) * 0.45, W / 2, H / 2, Math.hypot(W, H) * 0.62);
    g.addColorStop(0, 'rgba(0,0,0,0)'); g.addColorStop(1, 'rgba(0,0,0,0.38)');
    ctx2.fillStyle = g; ctx2.fillRect(0, 0, W, H);
}

// ---------------------------------------------------------------- main draw
function draw(target, dt, profile) {
    if (!skyCv) return;
    const density = densityFor(profile);
    if (density !== lastDensity) { buildDrops(density); ensureFlakes(density); lastDensity = density; }
    animTime += dt;

    // ease the scene toward the weather targets (so a weather change is a slow cross-fade, never a pop)
    const rate = 0.55;
    S.cloud = approach(S.cloud, TGT.cloud, dt, rate); S.rain = approach(S.rain, TGT.rain, dt, rate);
    S.snow = approach(S.snow, TGT.snow, dt, rate); S.fog = approach(S.fog, TGT.fog, dt, rate * 0.7);
    S.storm = approach(S.storm, TGT.storm, dt, rate); S.windX = approach(S.windX, TGT.windX, dt, 0.4);
    S.wind = approach(S.wind, TGT.wind, dt, 0.35); S.dir = approach(S.dir, TGT.dir, dt, 0.3);
    BFN = beaufort(S.wind);
    S.snowCover = approach(S.snowCover, TGT.snow > 0.3 ? Math.min(1, TGT.snow) : 0, dt, 0.06);

    const t = localMinutes();
    const { sr, ss } = sunTimes(offsetMinutes());
    const sky = skyAt(t, sr, ss);

    drawLightning(dt, sky);

    // shooting stars
    if (sky.amb < 0.2 && S.cloud < 0.4 && S.rain < 0.1) {
        nextShootIn -= dt;
        if (nextShootIn <= 0 && !shoot) {
            nextShootIn = 14 + Math.random() * 26;
            shoot = { x: W * (0.2 + Math.random() * 0.6), y: HORIZON * (0.05 + Math.random() * 0.3), dx: (Math.random() < 0.5 ? -1 : 1) * W * 0.22, dy: HORIZON * 0.12, life: 0, dur: 0.9 };
        }
    }
    if (shoot) { shoot.life += dt; if (shoot.life >= shoot.dur) shoot = null; }

    // ---- sky canvas
    const graded = drawSkyLayer(sky, t, sr, ss);
    drawStarsLayer(sky, 1 - S.cloud * 0.8 - S.fog * 0.5);
    const moon = drawMoonLayer(t, sr, ss, 1);
    const sun = drawSunLayer(sky, t, sr, ss);
    // the brighter visible body drives the light path on the water
    celest = null;
    if (sun && sun.vis > 0.05 && sun.e > 0.0) celest = { x: sun.x, y: sun.y, r: sun.r, power: clamp(sun.vis * (0.55 + 0.45 * sky.glow), 0, 1), col: mix([255, 255, 255], [255, 170, 110], clamp(1 - sun.e * 2.2, 0, 1)) };
    else if (moon && moon.alpha > 0.05) celest = { x: moon.x, y: moon.y, r: moon.r, power: clamp(moon.alpha * 0.9, 0, 1), col: [205, 220, 248] };
    drawCloudsLayer(sky, dt);
    if (flash > 0) { skyCtx.fillStyle = `rgba(200,215,255,${flash * 0.55})`; skyCtx.fillRect(0, 0, W, HORIZON); }
    drawHillsLayer(sky, graded);

    // ---- composite
    const useGlass = cfg.glass && glass.length > 0;
    let c2 = target;
    if (useGlass) {
        if (!sceneCv || sceneCv.width !== W || sceneCv.height !== H) { sceneCv = makeCanvas(W, H); sceneCtx = sceneCv.getContext('2d'); }
        c2 = sceneCtx;
    }
    c2.setTransform(1, 0, 0, 1, 0, 0);
    c2.globalAlpha = 1; c2.globalCompositeOperation = 'source-over';
    c2.drawImage(skyCv, 0, 0);
    drawWater(c2, sky, graded);
    drawRipples(c2, dt, sky.amb);
    drawFog(c2, dt, graded, sky.amb);
    drawRain(c2, dt, sky.amb, density);
    drawSnow(c2, dt, sky.amb);
    if (flash > 0) { c2.fillStyle = `rgba(210,222,255,${flash * 0.35})`; c2.fillRect(0, 0, W, H); }
    drawBolt(c2);
    drawVignette(c2);

    if (useGlass) {
        target.setTransform(1, 0, 0, 1, 0, 0);
        target.drawImage(sceneCv, 0, 0);
        updateGlass(dt);
        drawGlass(target, sceneCv);
    } else {
        updateGlass(dt);
    }
}

window.WeatherWallpaper = {
    setConfig, setWeather, resize,
    draw: (ctx2, dt, profile) => draw(ctx2, Math.min(dt, 0.1), profile),
    // info overlay helpers (clock / date / temperature drawn by index.html)
    info: () => {
        const m = localMinutes();
        const d = new Date(Date.now() + (offsetMinutes() + new Date().getTimezoneOffset()) * 60000);   // wall-clock date at the location
        return { h: Math.floor(m / 60) % 24, m: Math.floor(m % 60), date: d, temp: data && data.ok ? data.temp : null, code: cfg.weatherSim && cfg.weatherSim !== 'Auto' ? ({ Clear: 0, PartlyCloudy: 2, Cloudy: 3, Drizzle: 51, Rain: 63, HeavyRain: 65, Thunderstorm: 95, Snow: 73, Fog: 45 })[cfg.weatherSim] : (data && data.ok ? data.code : null) };
    },
    // test helpers (headless screenshots)
    _state: () => ({ S, TGT, bf: BFN, t: localMinutes(), sun: sunTimes(offsetMinutes()) }),
    _bolt: () => { nextBoltIn = 0; },
    _warm: (secs, ctx2, profile) => { for (let i = 0; i < secs * 30; i++) draw(ctx2, 1 / 30, profile); },
};
})();
