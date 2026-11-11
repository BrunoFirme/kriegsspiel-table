local compat = compat
local RAY, SPHERE, BOX = 1, 2, 3
local FAR, THICK, TINY, ROUNDS = 1000, 0.2, 1e-9, 60
local DEBUG_SHORTEST, DEBUG_PER_UNIT = 0.5, 0.025

local function v3(x, y, z) return { x = x, y = y, z = z } end
local function add(a, b) return v3(a.x + b.x, a.y + b.y, a.z + b.z) end
local function sub(a, b) return v3(a.x - b.x, a.y - b.y, a.z - b.z) end
local function mul(a, k) return v3(a.x * k, a.y * k, a.z * k) end
local function dot(a, b) return a.x * b.x + a.y * b.y + a.z * b.z end
local function cross(a, b) return v3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x) end
local function len(a) return math.sqrt(dot(a, a)) end
local function unit(a)
  local l = len(a)
  if l < TINY then return nil end
  return mul(a, 1 / l)
end

local function euler_axes(e)
  local x, y, z = math.rad(e.x), math.rad(e.y), math.rad(e.z)
  local cx, sx, cy, sy, cz, sz = math.cos(x), math.sin(x), math.cos(y), math.sin(y), math.cos(z), math.sin(z)
  local function turn(v)
    local a = v3(v.x * cz - v.y * sz, v.x * sz + v.y * cz, v.z)
    local b = v3(a.x, a.y * cx - a.z * sx, a.y * sx + a.z * cx)
    return v3(b.x * cy + b.z * sy, b.y, -b.x * sy + b.z * cy)
  end
  return { turn(v3(1, 0, 0)), turn(v3(0, 1, 0)), turn(v3(0, 0, 1)) }
end

local function piece_box(p)
  local s = p.size
  local o, ax, az = p.to_world(0, 0), p.to_world(1, 0), p.to_world(0, 1)
  local c, m = compat.position(p), p.middle
  return {
    center = v3(c.x + m.x - p.pos.x, c.y, c.z + m.z - p.pos.z),
    axes = { unit(v3(ax.x - o.x, 0, ax.z - o.z)) or v3(1, 0, 0), v3(0, 1, 0), unit(v3(az.x - o.x, 0, az.z - o.z)) or v3(0, 0, 1) },
    half = { s.w / 2, THICK * (p.scale or 1) / 2, s.h / 2 },
  }
end

local function reach(box, l)
  local r = 0
  for i = 1, 3 do r = r + box.half[i] * math.abs(dot(box.axes[i], l)) end
  return r
end

local function sweep(a, b, d, far)
  local axes = {}
  for i = 1, 3 do axes[#axes + 1] = b.axes[i] end
  for i = 1, 3 do axes[#axes + 1] = a.axes[i] end
  for i = 1, 3 do
    for j = 1, 3 do
      local c = unit(cross(a.axes[i], b.axes[j]))
      if c then axes[#axes + 1] = c end
    end
  end
  local t_in, t_out, normal = -math.huge, math.huge, nil
  for _, l in ipairs(axes) do
    local gap, speed, r = dot(sub(b.center, a.center), l), dot(d, l), reach(a, l) + reach(b, l)
    if math.abs(speed) < TINY then
      if math.abs(gap) > r then return nil end
    else
      local t1, t2 = (gap - r) / speed, (gap + r) / speed
      if t1 > t2 then t1, t2 = t2, t1 end
      if t1 > t_in then t_in, normal = t1, speed > 0 and mul(l, -1) or l end
      if t2 < t_out then t_out = t2 end
      if t_in > t_out then return nil end
    end
  end
  if t_out < 0 or t_in > far then return nil end
  return t_in, normal
end

local function nearest_on(box, p)
  local rel = sub(p, box.center)
  local out = box.center
  for i = 1, 3 do
    local k = math.max(-box.half[i], math.min(box.half[i], dot(rel, box.axes[i])))
    out = add(out, mul(box.axes[i], k))
  end
  return out
end

local function sphere_sweep(o, r, d, b, far)
  local grown = { center = b.center, axes = b.axes, half = { b.half[1] + r, b.half[2] + r, b.half[3] + r } }
  local point = { center = o, axes = b.axes, half = { 0, 0, 0 } }
  local lo = sweep(point, grown, d, far)
  if lo == nil then return nil end
  local function gap(t) local c = add(o, mul(d, t)) return len(sub(c, nearest_on(b, c))) - r end
  if gap(math.max(0, lo)) <= 0 then return math.max(0, lo) end
  local a, z = math.max(0, lo), far
  for _ = 1, ROUNDS do
    local m1, m2 = a + (z - a) / 3, z - (z - a) / 3
    if gap(m1) < gap(m2) then z = m2 else a = m1 end
  end
  local best = (a + z) / 2
  if gap(best) > 0 then return nil end
  local hi, low = best, math.max(0, lo)
  for _ = 1, ROUNDS do
    local m = (low + hi) / 2
    if gap(m) <= 0 then hi = m else low = m end
  end
  return hi
end

local function hit_of(kind, o, d, size, turnto, far, b)
  if kind == SPHERE then
    local r = math.abs(size.x) / 2
    local t = sphere_sweep(o, r, d, b, far)
    if t == nil then return nil end
    if t <= 0 then return 0, nil, nil end
    local c = add(o, mul(d, t))
    local at = nearest_on(b, c)
    return t, at, unit(sub(c, at)) or mul(d, -1)
  end
  local caster = { center = o, axes = kind == BOX and euler_axes(turnto) or { v3(1, 0, 0), v3(0, 1, 0), v3(0, 0, 1) },
    half = kind == BOX and { math.abs(size.x) / 2, math.abs(size.y) / 2, math.abs(size.z) / 2 } or { 0, 0, 0 } }
  local t, normal = sweep(caster, b, d, far)
  if t == nil then return nil end
  if kind == RAY then
    if t < 0 then return nil end
    return t, add(o, mul(d, t)), normal
  end
  if t <= 0 then return 0, nil, nil end
  return t, nearest_on(b, add(o, mul(d, t))), normal
end

local function show(kind, o, d, size, turnto, far)
  local reach_to = math.min(far, FAR)
  local to = add(o, mul(d, reach_to))
  board.show_cast({ shape = kind == SPHERE and "sphere" or kind == BOX and "box" or "ray", from = o, to = to, size = size, turn = turnto,
    seconds = math.max(DEBUG_PER_UNIT * reach_to, DEBUG_SHORTEST) })
end

function compat.cast(p)
  if type(p) ~= "table" then return nil end
  local kind = tonumber(p.type) or RAY
  if kind ~= SPHERE and kind ~= BOX then kind = RAY end
  local function given(v) if v == nil then return v3(0, 0, 0) end return compat.vector_arg(v) end
  local o = given(p.origin)
  local raw = given(p.direction)
  local size, turnto = given(p.size), given(p.orientation)
  local far = tonumber(p.max_distance) or FAR
  local d = unit(v3(raw.x, raw.y, raw.z))
  local origin = v3(o.x, o.y, o.z)
  if p.debug == true and d then show(kind, origin, d, size, turnto, far) end
  local found = {}
  if d == nil and kind == RAY then return found end
  for i, piece in ipairs(board.pieces()) do
    local t, at, normal = hit_of(kind, origin, d or v3(0, 0, 0), size, turnto, far, piece_box(piece))
    if t then found[#found + 1] = { i = i, t = t, at = at, normal = normal, piece = piece } end
  end
  table.sort(found, function(a, b) if a.t ~= b.t then return a.t < b.t end return a.i < b.i end)
  local back = d and mul(d, -1) or v3(0, 0, 0)
  local hits = {}
  for k, h in ipairs(found) do
    local at, n = h.at or v3(0, 0, 0), h.normal or back
    hits[k] = { hit_object = h.piece, distance = h.t, point = compat.Vector(at.x, at.y, at.z), normal = compat.Vector(n.x, n.y, n.z) }
  end
  return hits
end
