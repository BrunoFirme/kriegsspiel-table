local compat = compat

local Vector = { __isVector = true, __version = "1.0.1" }
local AXES = { "x", "y", "z" }
function Vector.new(...)
  local v = setmetatable({ x = 0, y = 0, z = 0 }, Vector)
  local n = select("#", ...)
  if n == 3 then
    v.x, v.y, v.z = ...
  elseif n == 1 and type(...) == "table" then
    local src = ...
    v.x = src.x or src[1] or v.x
    v.y = src.y or src[2] or v.y
    v.z = src.z or src[3] or v.z
  end
  return v
end
setmetatable(Vector, { __call = function(_, ...) return Vector.new(...) end })
function Vector:__index(k)
  k = AXES[k] or k
  return rawget(self, k) or Vector[k]
end
function Vector:__newindex(k, v) return rawset(self, AXES[k] or k, v) end
function Vector:setAt(k, v)
  self[k] = v
  return self
end
function Vector:set(x, y, z)
  self.x = x or self.x
  self.y = y or self.y
  self.z = z or self.z
  return self
end
function Vector:get() return self.x, self.y, self.z end
function Vector:copy() return Vector(self) end
function Vector:add(o)
  self.x, self.y, self.z = self.x + o.x, self.y + o.y, self.z + o.z
  return self
end
function Vector.__add(a, b) return a:copy():add(b) end
function Vector:sub(o)
  self.x, self.y, self.z = self.x - o.x, self.y - o.y, self.z - o.z
  return self
end
function Vector.__sub(a, b) return a:copy():sub(b) end
function Vector:dot(o) return self.x * o.x + self.y * o.y + self.z * o.z end
function Vector:sqrMagnitude() return self:dot(self) end
function Vector:magnitude() return math.sqrt(self:dot(self)) end
function Vector:scale(...)
  local sx, sy, sz
  local n = select("#", ...)
  if n == 1 then
    local k = ...
    if type(k) == "number" then sx, sy, sz = k, k, k else sx, sy, sz = k:get() end
  elseif n == 3 then
    sx, sy, sz = ...
    sx, sy, sz = sx or 1, sy or 1, sz or 1
  end
  self.x, self.y, self.z = self.x * sx, self.y * sy, self.z * sz
  return self
end
function Vector.__mul(a, b)
  if type(a) == "number" then a, b = b, a end
  return a:copy():scale(b)
end
function Vector:sqrDistance(o)
  local dx, dy, dz = self.x - o.x, self.y - o.y, self.z - o.z
  return dx * dx + dy * dy + dz * dz
end
function Vector:distance(o) return math.sqrt(self:sqrDistance(o)) end
local VECTOR_MARGIN = 1e-3
function Vector:equals(o, margin) return self:sqrDistance(o) <= (margin or VECTOR_MARGIN) end
Vector.__eq = Vector.equals
local function trimmed(s) return (s:gsub("%.0+([ ,])", "%1"):gsub("%.(%d-)0+([ ,])", ".%1%2")) end
function Vector:string(prefix)
  prefix = prefix and (prefix .. ": ") or ""
  return trimmed(string.format("%s{ %f, %f, %f }", prefix, self.x, self.y, self.z))
end
function Vector:__tostring() return self:string("Vector") end
function Vector:angle(o) return math.deg(math.acos(self:dot(o) / (self:magnitude() * o:magnitude()))) end
function Vector:clamp(most)
  local len = self:magnitude()
  if len <= most then return self end
  return self:scale(most / len)
end
function Vector:cross(o) return Vector(self.y * o.z - self.z * o.y, self.z * o.x - self.x * o.z, self.x * o.y - self.y * o.x) end
function Vector.between(from, to) return to - from end
function Vector:lerp(target, t) return self:between(target):scale(t):add(self) end
function Vector:moveTowards(target, most) return self:add(self:between(target):clamp(most)) end
function Vector:normalize()
  local sq = self:sqrMagnitude()
  if sq == 1 or sq == 0 then return self end
  return self:scale(1 / math.sqrt(sq))
end
function Vector:normalized() return self:copy():normalize() end
function Vector:project(o)
  if o:sqrMagnitude() ~= 1 then o = o:normalized() end
  local k = self:dot(o)
  return self:set(o:get()):scale(k)
end
local function apply(m, v) return v:set(m[1]:dot(v), m[2]:dot(v), m[3]:dot(v)) end
local function skew(v) return { Vector(0, -v.z, v.y), Vector(v.z, 0, -v.x), Vector(-v.y, v.x, 0) } end
local function times(a, b)
  local cols = { Vector(b[1].x, b[2].x, b[3].x), Vector(b[1].y, b[2].y, b[3].y), Vector(b[1].z, b[2].z, b[3].z) }
  local out = { Vector(), Vector(), Vector() }
  for r = 1, 3 do out[r]:set(a[r]:dot(cols[1]), a[r]:dot(cols[2]), a[r]:dot(cols[3])) end
  return out
end
local OPPOSITE, SLERP_EDGE = 0.05, 0.0005
local function turn_matrix(from, to)
  local k = skew(from:cross(to))
  local d = from:dot(to)
  assert(math.abs(d + 1) > OPPOSITE, "Vectors too close to opposite of each other")
  local k2 = times(k, k)
  for _, row in ipairs(k2) do row:scale(1 / (1 + d)) end
  local m = { Vector(1, 0, 0), Vector(0, 1, 0), Vector(0, 0, 1) }
  for r = 1, 3 do m[r]:add(k[r]):add(k2[r]) end
  return m
end
local function toward(x, y, z, w, t)
  if w > SLERP_EDGE then
    x, y, z, w = t * x, t * y, t * z, 1 + t * w - t
    local n = math.sqrt(x * x + y * y + z * z + w * w)
    return x / n, y / n, z / n, w / n
  end
  local th0 = math.acos(w)
  local th = th0 * t
  local s1 = math.sin(th) / math.sin(th0)
  local s0 = math.cos(th) - w * s1
  return x * s1, y * s1, z * s1, (w + s0) * s1
end
local function part_of(m, t)
  local w = math.sqrt(1 + m[1].x + m[2].y + m[3].z) / 2
  local x, y, z = (m[3].y - m[2].z) / (w * 4), (m[1].z - m[3].x) / (w * 4), (m[2].x - m[1].y) / (w * 4)
  x, y, z, w = toward(x, y, z, w, t)
  return { Vector(1 - 2 * y * y - 2 * z * z, 2 * x * y - 2 * z * w, 2 * x * z + 2 * y * w),
    Vector(2 * x * y + 2 * z * w, 1 - 2 * x * x - 2 * z * z, 2 * y * z - 2 * x * w),
    Vector(2 * x * z - 2 * y * w, 2 * y * z + 2 * x * w, 1 - 2 * x * x - 2 * y * y) }
end
function Vector:rotateTowardsUnit(unit, most)
  local m = turn_matrix(self, unit)
  if most then
    local angle = math.deg(math.acos((m[1].x + m[2].y + m[3].z - 1) / 2))
    if angle > most then m = part_of(m, most / angle) end
  end
  return apply(m, self)
end
function Vector:rotateTowards(target, most)
  local len = self:magnitude()
  return self:normalize():rotateTowardsUnit(target:normalized(), most):scale(len)
end
function Vector:reflect(n)
  local x, y, z = n:normalized():get()
  return apply({ Vector(1 - 2 * x * x, -2 * x * y, -2 * x * z), Vector(-2 * x * y, 1 - 2 * y * y, -2 * y * z), Vector(-2 * x * z, -2 * y * z, 1 - 2 * z * z) }, self)
end
function Vector:rotateOver(axis, angle)
  angle = math.rad(angle)
  local s, c = math.sin(angle), math.cos(angle)
  local m
  if axis == "x" then m = { Vector(1, 0, 0), Vector(0, c, -s), Vector(0, s, c) }
  elseif axis == "y" then m = { Vector(c, 0, s), Vector(0, 1, 0), Vector(-s, 0, c) }
  elseif axis == "z" then m = { Vector(c, -s, 0), Vector(s, c, 0), Vector(0, 0, 1) } end
  return apply(m, self)
end
function Vector.max(a, b) return Vector(math.max(a.x, b.x), math.max(a.y, b.y), math.max(a.z, b.z)) end
function Vector.min(a, b) return Vector(math.min(a.x, b.x), math.min(a.y, b.y), math.min(a.z, b.z)) end
function Vector:inverse() return self:set(-1 * self.x, -1 * self.y, -1 * self.z) end
local NEAR_AXIS, RIGHT_ANGLE = 10, 90
function Vector:orthoNormalize(planar)
  if not planar then
    planar = Vector(1, 0, 0)
    if self:angle(planar) < NEAR_AXIS then planar = Vector(0, 0, 1) end
  elseif planar:sqrMagnitude() ~= 1 then
    planar = planar:normalized()
  end
  local base = self:normalized()
  local normal = base:cross(planar:normalized())
  return base, normal, base:cross(normal)
end
function Vector:projectOnPlane(n)
  local _, _, shadow = n:orthoNormalize(self)
  if self:angle(shadow) > RIGHT_ANGLE then shadow:inverse() end
  return self:project(shadow)
end
function Vector:heading(axis)
  if not axis then return self:heading("x"), self:heading("y"), self:heading("z") end
  local a, b
  if axis == "x" then a, b = self.y, self.z elseif axis == "y" then a, b = self.x, self.z elseif axis == "z" then a, b = self.x, self.y end
  return math.deg(math.atan2(a, b))
end
compat.Vector = Vector

local Color = { __isColor = true, __version = "1.0.1" }
local CHANNELS = { "r", "g", "b", "a" }
local function clamp01(v) return math.min(1, math.max(0, v or 0)) end
function Color.new(...)
  local c = setmetatable({ r = 0, g = 0, b = 0, a = 1 }, Color)
  local n = select("#", ...)
  if n == 1 and type(...) == "table" then
    local src = ...
    c.r, c.g, c.b, c.a = clamp01(src.r or c.r), clamp01(src.g or c.g), clamp01(src.b or c.b), clamp01(src.a or c.a)
  elseif n == 3 then
    local r, g, b = ...
    c.r, c.g, c.b = clamp01(r), clamp01(g), clamp01(b)
  elseif n == 4 then
    local r, g, b, a = ...
    c.r, c.g, c.b, c.a = clamp01(r), clamp01(g), clamp01(b), clamp01(a)
  end
  return c
end
local function title(s) return s:sub(1, 1):upper() .. s:sub(2, -1):lower() end
local SEAT_RGB = { white = { 1, 1, 1 }, brown = { 0.443, 0.231, 0.09 }, red = { 0.856, 0.1, 0.094 }, orange = { 0.956, 0.392, 0.113 },
  yellow = { 0.905, 0.898, 0.172 }, green = { 0.192, 0.701, 0.168 }, teal = { 0.129, 0.694, 0.607 }, blue = { 0.118, 0.53, 1 },
  purple = { 0.627, 0.125, 0.941 }, pink = { 0.96, 0.439, 0.807 }, grey = { 0.5, 0.5, 0.5 }, black = { 0.25, 0.25, 0.25 } }
local seats = {}
for name, c in pairs(SEAT_RGB) do seats[name] = Color.new(c[1], c[2], c[3]) end
Color.list = {}
for name in pairs(seats) do table.insert(Color.list, title(name)) end
setmetatable(Color, {
  __call = function(_, ...) return Color.new(...) end,
  __index = function(_, name)
    name = name:lower()
    if seats[name] then return seats[name]:copy() end
    return nil
  end,
})
function Color.Add(name, c)
  name = name:lower()
  assert(not seats[name], "Color " .. name .. " already defined")
  assert(c.__isColor, tostring(c) .. " is not a Color instance")
  seats[name] = c
  table.insert(Color.list, title(name))
end
function Color.fromString(s) return assert(seats[s:lower()], s .. " is not a valid color string"):copy() end
function Color.fromHex(hex)
  local r, g, b, a = hex:match("^#?(%x%x)(%x%x)(%x%x)(%x?%x?)$")
  assert(r and g and b and (a:len() == 0 or a:len() == 2), tostring(hex) .. " is not a valid color hex string")
  local alpha = 1
  if a ~= "" then alpha = tonumber(a, 16) / 255 end
  return Color(tonumber(r, 16) / 255, tonumber(g, 16) / 255, tonumber(b, 16) / 255, alpha)
end
function Color:get() return self.r, self.g, self.b, self.a end
local function byte_of(v) return math.floor(v * 255) end
function Color:toHex(alpha)
  if alpha then return string.format("%02x%02x%02x%02x", byte_of(self.r), byte_of(self.g), byte_of(self.b), byte_of(self.a)) end
  return string.format("%02x%02x%02x", byte_of(self.r), byte_of(self.g), byte_of(self.b))
end
function Color:toString(tolerance)
  for name, c in pairs(seats) do
    if self:equals(c, tolerance) then return title(name) end
  end
  return nil
end
function Color:__index(k)
  k = CHANNELS[k] or k
  return rawget(self, k) or Color[k]
end
function Color:__newindex(k, v) return rawset(self, CHANNELS[k] or k, v) end
function Color:set(r, g, b, a)
  self.r, self.g, self.b, self.a = clamp01(r or self.r), clamp01(g or self.g), clamp01(b or self.b), clamp01(a or self.a)
end
function Color:setAt(k, v)
  self[k] = clamp01(v)
  return self
end
local COLOR_MARGIN = 1e-2
function Color:equals(o, margin)
  local d = math.abs(self.r - o.r) + math.abs(self.g - o.g) + math.abs(self.b - o.b) + math.abs(self.a - o.a)
  return d <= (margin or COLOR_MARGIN)
end
Color.__eq = Color.equals
function Color:copy() return Color(self:get()) end
function Color:dump(prefix)
  local name = self:toString()
  local form = self.a < 1 and "%s%s{ r = %f, g = %f, b = %f, a = %f }" or "%s%s{ r = %f, g = %f, b = %f }"
  return trimmed(string.format(form, prefix and (prefix .. ": ") or "", name and (name .. " ") or "", self:get()))
end
function Color:__tostring() return self:dump("Color") end
function Color:lerp(o, t)
  local mid = self:copy()
  mid.r, mid.g, mid.b, mid.a = mid.r + (o.r - self.r) * t, mid.g + (o.g - self.g) * t, mid.b + (o.b - self.b) * t, mid.a + (o.a - self.a) * t
  return mid
end
compat.Color = Color
function compat.construct_color(r, g, b, a)
  if tonumber(r) == nil or tonumber(g) == nil or tonumber(b) == nil then error("Error in color: You must specify all of r, g, b.", 0) end
  return setmetatable({ r = tonumber(r), g = tonumber(g), b = tonumber(b), a = tonumber(a) or 1 }, Color)
end
function compat.construct_vector(x, y, z)
  if tonumber(x) == nil or tonumber(y) == nil or tonumber(z) == nil then error("Error in vector: You must specify all of x, y, z.", 0) end
  return Vector(tonumber(x), tonumber(y), tonumber(z))
end
function compat.hex_of(c)
  local function byte(v) return math.floor(math.max(0, math.min(1, v)) * 255 + 0.5) end
  return string.format("#%02x%02x%02x%02x", byte(c.r), byte(c.g), byte(c.b), byte(c.a))
end

local VECTOR_ERROR = "Error converting Table to Vector. (x, y, z or 1, 2, 3)"
local COLOR_ERROR = "Error converting Table to Color. (r, g, b or 1, 2, 3)"
local LABEL_ERROR = "Error converting String to Color. (White, Red, Blue, etc.)"
local LABELS = { White = { 1, 1, 1 }, Brown = { 0.443, 0.231, 0.09 }, Red = { 0.856, 0.1, 0.094 }, Orange = { 0.956, 0.392, 0.113 },
  Yellow = { 0.905, 0.898, 0.172 }, Green = { 0.192, 0.701, 0.168 }, Teal = { 0.129, 0.694, 0.607 }, Blue = { 0.118, 0.53, 1 },
  Purple = { 0.627, 0.125, 0.941 }, Pink = { 0.96, 0.439, 0.807 }, Grey = { 0.5, 0.5, 0.5 }, Black = { 0.25, 0.25, 0.25 } }
local FULL_TURN, HALF_TURN, FLIP_INDEX = 360, 180, 12
local TAKE_OFFSET = 2

local function clr(kind, v) return "cannot convert a " .. type(v) .. " to a clr type " .. kind end

local function single(v, err)
  if type(v) == "number" then return v end
  if type(v) == "boolean" then return v and 1 or 0 end
  local n = tonumber(v)
  if n == nil then error(err, 0) end
  return n
end

function compat.vector_arg(t)
  if type(t) ~= "table" then error(clr("UnityEngine.Vector3", t), 0) end
  if t.x ~= nil and t.y ~= nil and t.z ~= nil then return compat.Vector(single(t.x, VECTOR_ERROR), single(t.y, VECTOR_ERROR), single(t.z, VECTOR_ERROR)) end
  if t[1] ~= nil and t[2] ~= nil and t[3] ~= nil then return compat.Vector(single(t[1], VECTOR_ERROR), single(t[2], VECTOR_ERROR), single(t[3], VECTOR_ERROR)) end
  if t.x ~= nil and t.y ~= nil then return compat.Vector(single(t.x, VECTOR_ERROR), single(t.y, VECTOR_ERROR), 0) end
  if t[1] ~= nil and t[2] ~= nil then return compat.Vector(single(t[1], VECTOR_ERROR), single(t[2], VECTOR_ERROR), 0) end
  error(VECTOR_ERROR, 0)
end

function compat.optional_vector_arg(t)
  if t == nil then return nil end
  return compat.vector_arg(t)
end

local function label_color(name)
  local c = LABELS[name]
  if c == nil then return nil end
  return compat.Color(c[1], c[2], c[3], 1)
end

function compat.color_arg(c, nullable)
  if c == nil and nullable then return nil end
  if type(c) == "string" then
    local named = label_color(c)
    if named == nil and not nullable then error(LABEL_ERROR, 0) end
    return named
  end
  if type(c) ~= "table" then error(clr("UnityEngine.Color", c), 0) end
  if c.r ~= nil and c.g ~= nil and c.b ~= nil then
    local a = 1
    if c.a ~= nil then a = single(c.a, COLOR_ERROR) end
    return compat.Color(single(c.r, COLOR_ERROR), single(c.g, COLOR_ERROR), single(c.b, COLOR_ERROR), a)
  end
  if c[1] ~= nil and c[2] ~= nil and c[3] ~= nil then
    local a = 1
    if c[4] ~= nil then a = single(c[4], COLOR_ERROR) end
    return compat.Color(single(c[1], COLOR_ERROR), single(c[2], COLOR_ERROR), single(c[3], COLOR_ERROR), a)
  end
  error(COLOR_ERROR, 0)
end

function compat.string_color_to_rgb(name)
  if type(name) ~= "string" then return nil end
  return label_color(name)
end

local function turn(deg) return ((deg % FULL_TURN) + FULL_TURN) % FULL_TURN end

local STEP, PULL, FAST_PULL, DAMP, FAST_DAMP, TORQUE, SPIN_DAMP = 0.02, 10000, 100000, 20, 40, 0.045, 13
local NEAR, NEAR_TURN, MOST_STEPS = 0.025, 1, 2000
local gliding = {}

local function heading_of(r)
  local x = math.rad(r.x)
  local heading = r.y
  if math.cos(x) < 0 then heading = r.y + HALF_TURN end
  return turn(heading)
end
local function down_of(r) return math.cos(math.rad(r.x)) * math.cos(math.rad(r.z)) < 0 end
local function apart(a, b) local d = math.abs(turn(a) - turn(b)) return math.min(d, FULL_TURN - d) end

local function lie(obj, r)
  local down = down_of(r)
  if down ~= (obj.face_down == true) then obj.flip(down) end
  obj.set_rot(heading_of(r))
end

function compat.position(obj)
  local g = gliding[obj.id]
  if g and g.at then return compat.Vector(g.at.x, obj.y or 0, g.at.z) end
  local p = obj.pos
  return compat.Vector(p.x, obj.y or 0, p.z)
end
function compat.rotation(obj)
  local g = gliding[obj.id]
  if g and g.heading then return compat.Vector(0, turn(g.heading), g.down and HALF_TURN or 0) end
  return compat.Vector(0, turn(obj.rot or 0), obj.face_down and HALF_TURN or 0)
end

local function land(obj, g)
  gliding[obj.id] = nil
  if obj.exists == false then return end
  if g.to then obj.set_pos(g.to.x, g.to.z) end
  if g.turn_to then lie(obj, g.turn_to) end
  for _, f in ipairs(g.after) do f() end
end

local function step(g)
  if g.at then
    local m = 1
    local pull, damp = g.fast and FAST_PULL or PULL, g.fast and FAST_DAMP or DAMP
    local keep = math.max(0, 1 - STEP * damp / ((m + 1) / 2))
    for _, k in ipairs({ "x", "z" }) do
      g.v[k] = (g.v[k] + (g.to[k] - g.at[k]) * STEP * pull * STEP / m) * keep
      g.at[k] = g.at[k] + g.v[k] * STEP
    end
    if math.sqrt((g.to.x - g.at.x) ^ 2 + (g.to.z - g.at.z) ^ 2) < NEAR then g.at = nil end
  end
  if g.heading then
    local left = apart(g.heading, g.goal)
    local dir = turn(g.goal - g.heading) <= HALF_TURN and 1 or -1
    g.spin = (g.spin + TORQUE * left) * (1 - SPIN_DAMP * STEP)
    g.heading = g.heading + dir * math.deg(g.spin * STEP)
    if apart(g.heading, g.goal) < NEAR_TURN then g.heading = nil end
  end
end

local function run(obj, g)
  local function frame()
    if gliding[obj.id] ~= g then return end
    if obj.exists == false or obj.pos.x ~= g.from.x or obj.pos.z ~= g.from.z then
      gliding[obj.id] = nil
      return
    end
    local due = math.min(MOST_STEPS, math.floor((wait.now() - g.started) / STEP + 1e-9))
    while g.done < due and (g.at or g.heading) do
      step(g)
      g.done = g.done + 1
    end
    if g.at == nil and g.heading == nil then land(obj, g) else wait.frames(1, frame) end
  end
  wait.frames(1, frame)
end

function compat.glide(obj, to, turn_to, fast, after)
  local here = obj.pos
  local g = gliding[obj.id] or { v = { x = 0, z = 0 }, spin = 0, after = {}, done = 0, started = wait.now(), from = { x = here.x, z = here.z } }
  g.fast = fast == true
  if to and math.sqrt((to.x - here.x) ^ 2 + (to.z - here.z) ^ 2) > NEAR then
    g.to = { x = to.x, z = to.z }
    g.at = g.at or { x = here.x, z = here.z }
  end
  if turn_to then
    local goal = heading_of(turn_to)
    local from = g.heading or obj.rot or 0
    if apart(from, goal) > NEAR_TURN or down_of(turn_to) ~= (obj.face_down == true) then
      g.turn_to, g.goal, g.heading, g.down = turn_to, goal, from, obj.face_down == true
    end
  end
  if after then g.after[#g.after + 1] = after end
  if g.at == nil and g.heading == nil then
    if after then wait.frames(1, after) end
    return
  end
  local fresh = gliding[obj.id] ~= g
  gliding[obj.id] = g
  if fresh then run(obj, g) end
end
function compat.gliding(obj) return gliding[obj.id] ~= nil end
function compat.smooth_target(obj)
  local g = gliding[obj.id]
  if g and g.at then return compat.Vector(g.to.x, obj.y or 0, g.to.z) end
  return nil
end
function compat.smooth_turn_target(obj)
  local g = gliding[obj.id]
  if g and g.heading then return compat.Vector(0, g.goal, down_of(g.turn_to) and HALF_TURN or 0) end
  return nil
end
local function stop_glide(obj) gliding[obj.id] = nil end

function compat.set_rotation(obj, v)
  local r = compat.vector_arg(v)
  stop_glide(obj)
  lie(obj, r)
  return true
end
function compat.set_position(obj, v)
  local p = compat.vector_arg(v)
  stop_glide(obj)
  obj.set_pos(p.x, p.z)
  return true
end
function compat.set_position_smooth(obj, v, collide, fast)
  compat.glide(obj, compat.vector_arg(v), nil, fast)
  return true
end
function compat.set_rotation_smooth(obj, v, collide, fast)
  compat.glide(obj, nil, compat.vector_arg(v), fast)
  return true
end
function compat.rotate(obj, v)
  local by = compat.vector_arg(v)
  local now = compat.rotation(obj)
  compat.glide(obj, nil, compat.Vector(now.x + by.x, now.y + by.y, now.z + by.z), false)
  return true
end
function compat.translate(obj, v)
  local by = compat.vector_arg(v)
  local now = compat.position(obj)
  compat.glide(obj, compat.Vector(now.x + by.x, now.y + by.y, now.z + by.z), nil, false)
  return true
end

function compat.transform_forward(obj) local a = math.rad(obj.rot or 0); return compat.Vector(math.sin(a), 0, math.cos(a)) end
function compat.transform_right(obj)
  local a, k = math.rad(obj.rot or 0), obj.face_down and -1 or 1
  return compat.Vector(k * math.cos(a), 0, -k * math.sin(a))
end
function compat.transform_up(obj) return compat.Vector(0, obj.face_down and -1 or 1, 0) end

function compat.yes() return true end
function compat.set_name(obj, s) obj.set_name(s) return true end
function compat.set_description(obj, s) obj.set_note(s) return true end
function compat.set_memo(obj, s) obj.set_memo(s) return true end
function compat.set_lua_script(obj, s) obj.set_script(s) return true end
function compat.set_var(obj, name, v) obj.set_var(name, v) return true end
function compat.set_lock(obj, v) obj.set_locked(v and true or false) return true end
function compat.set_color_tint(obj, c)
  local col = compat.color_arg(c)
  obj.set_tint({ r = col.r, g = col.g, b = col.b, a = col.a })
  return true
end
function compat.color_tint(obj)
  local t = obj.tint
  local function hex2(s, i) return tonumber(s:sub(i, i + 1), 16) / 255 end
  if type(t) == "string" and #t >= 7 and t:sub(1, 1) == "#" then return compat.Color(hex2(t, 2), hex2(t, 4), hex2(t, 6), #t >= 9 and hex2(t, 8) or 1) end
  return compat.Color(1, 1, 1, 1)
end
function compat.destroy(obj)
  obj.delete()
  return true
end
function compat.flip(obj)
  local faces = obj.faces
  if faces == nil or #faces < 2 then obj.flip() return true end
  local now = faces[obj.face].up
  local best, most = obj.face, -2
  for i, f in ipairs(faces) do
    local d = -(f.up.x * now.x + f.up.y * now.y + f.up.z * now.z)
    if d > most then most = d best = i end
  end
  obj.set_face(best)
  return true
end
local set_custom_before = compat.set_custom_object
function compat.set_custom_object(obj, c)
  if type(c) ~= "table" then return false end
  if getmetatable(obj) == getmetatable(compat.spawn_object({})) and compat.piece_of(obj) == nil then
    set_custom_before(obj, c)
    return true
  end
  local p = compat.piece_of(obj)
  local waiting = compat.get_prop(p, "custom", nil) or {}
  for k, v in pairs(c) do waiting[k] = v end
  compat.set_prop(p, "custom", waiting)
  return true
end
function compat.reload(obj)
  local waiting = compat.get_prop(obj, "custom", nil)
  if waiting ~= nil then
    compat.set_prop(obj, "custom", nil)
    set_custom_before(obj, waiting)
  end
  obj.reload()
  return obj
end

function compat.state_id(obj)
  if #(obj.states or {}) < 2 then return -1 end
  return obj.state
end
function compat.set_state(obj, id)
  local n = #(obj.states or {})
  id = math.floor(tonumber(id) or 0)
  if n < 2 or id < 0 or id > n then return nil end
  if id == 0 or id == obj.state then
    print("setState: This state id does not exist")
    return nil
  end
  obj.set_state(id)
  return obj
end
function compat.states(obj)
  local names = obj.states or {}
  if #names < 2 then return nil end
  local t = {}
  for i, name in ipairs(names) do
    if i ~= obj.state then
      t[#t + 1] = { id = i, name = name, nickname = name, description = "", gm_notes = "", guid = compat.guid(obj), lua_script = "", lua_script_state = "", memo = "", tags = {} }
    end
  end
  return t
end

local function norm_tag(s) return tostring(s):lower():gsub(" ", "_") end
compat.norm_tag = norm_tag
function compat.has_tag(obj, tag)
  if tag == nil then return false end
  local want = norm_tag(tag)
  for _, t in ipairs(obj.tags or {}) do if norm_tag(t) == want then return true end end
  return false
end
function compat.has_any_tag(obj) return #(obj.tags or {}) > 0 end
function compat.has_matching_tag(obj, other)
  if type(other) ~= "table" then return false end
  for _, t in ipairs(obj.tags or {}) do if compat.has_tag(other, t) then return true end end
  return false
end
function compat.add_tag(obj, tag)
  if tag == nil then return false end
  if compat.has_tag(obj, tag) then return true end
  obj.add_tag(tostring(tag))
  return true
end
function compat.remove_tag(obj, tag)
  if tag == nil then return false end
  local want, kept, found = norm_tag(tag), {}, false
  for _, t in ipairs(obj.tags or {}) do
    if norm_tag(t) == want then found = true else kept[#kept + 1] = t end
  end
  if found then obj.set_tags(kept) end
  return found
end
function compat.set_tags(obj, list)
  local seen, kept = {}, {}
  for _, t in ipairs(list or {}) do
    local k = norm_tag(t)
    if not seen[k] then seen[k] = true kept[#kept + 1] = tostring(t) end
  end
  obj.set_tags(kept)
  return true
end
function compat.pieces_with_tag(tag)
  local out = {}
  for _, p in ipairs(board.pieces()) do if compat.has_tag(p, tag) then out[#out + 1] = p end end
  return out
end
function compat.pieces_with_any_tags(tags)
  local out = {}
  for _, p in ipairs(board.pieces()) do
    for _, t in ipairs(tags or {}) do if compat.has_tag(p, t) then out[#out + 1] = p break end end
  end
  return out
end
function compat.pieces_with_all_tags(tags)
  local out = {}
  for _, p in ipairs(board.pieces()) do
    local all = true
    for _, t in ipairs(tags or {}) do if not compat.has_tag(p, t) then all = false break end end
    if all then out[#out + 1] = p end
  end
  return out
end
function compat.all_tags()
  local seen, out = {}, {}
  for _, p in ipairs(board.pieces()) do
    for _, t in ipairs(p.tags or {}) do
      local k = norm_tag(t)
      if not seen[k] then seen[k] = true out[#out + 1] = t end
    end
  end
  return out
end

function compat.copy_lua_table(t, seen)
  if type(t) ~= "table" then return t end
  seen = seen or {}
  if seen[t] then return seen[t] end
  local out = {}
  seen[t] = out
  for k, v in pairs(t) do out[k] = compat.copy_lua_table(v, seen) end
  local mt = getmetatable(t)
  if mt == getmetatable(compat.Vector()) or mt == getmetatable(compat.Color()) then setmetatable(out, mt) end
  return out
end

function compat.call_in(target, fn, params, what)
  if params ~= nil and type(params) ~= "table" then error(clr("MoonSharp.Interpreter.Table", params), 0) end
  local results = table.pack(pcall(target.call, fn, compat.copy_lua_table(params)))
  if not results[1] then
    print((what or "call") .. "/" .. tostring(fn) .. ": " .. tostring(results[2]))
    return nil
  end
  local r = results[2]
  if type(r) == "table" then return compat.copy_lua_table(r) end
  return table.unpack(results, 2, results.n)
end
function compat.call(obj, fn, params) return compat.call_in(compat.piece_of(obj), fn, params) end
function compat.global_call(fn, params) return compat.call_in(game, fn, params) end
function compat.get_table(obj, name) return compat.copy_lua_table(obj.get_var(name)) end
function compat.set_table(obj, name, t) obj.set_var(name, compat.copy_lua_table(t)) return true end
function compat.global_get_table(name) return compat.copy_lua_table(game.get_var(name)) end
function compat.global_set_table(name, t) game.set_var(name, compat.copy_lua_table(t)) return true end
function compat.global_set_var(name, v) game.set_var(name, v) return true end

function compat.holds(obj) return obj.holds == true end
function compat.take_object(obj, params)
  params = type(params) == "table" and params or {}
  local position = compat.optional_vector_arg(params.position)
  local rotation = compat.optional_vector_arg(params.rotation)
  if not compat.holds(obj) then return nil end
  local here = compat.position(obj)
  position = position or compat.Vector(here.x + TAKE_OFFSET, here.y, here.z)
  rotation = rotation or compat.rotation(obj)
  local which = params.guid
  if which == nil and type(params.index) == "number" then which = math.floor(params.index) + 1 end
  local smooth = params.smooth ~= false
  local start = smooth and { x = here.x, z = here.z } or { x = position.x, z = position.z }
  local p = obj.unstow(which, start, smooth and turn(obj.rot or 0) or heading_of(rotation))
  if p == nil then return nil end
  if not smooth then
    if down_of(rotation) ~= (p.face_down == true) then p.flip() end
    compat.after_spawn(obj, p, params)
    return p
  end
  compat.glide(p, position, rotation, false, function() compat.call_back(obj, p, params) end)
  return p
end
function compat.call_back(obj, p, params)
  local cb = params.callback_function
  if cb ~= nil and type(cb) ~= "function" then
    print("Error converting Lua Table entry callback_function to Type MoonSharp.Interpreter.Closure. Value = " .. tostring(cb))
    cb = nil
  end
  if cb ~= nil then return cb(p) end
  local name = params.callback
  if type(name) ~= "string" or name == "" then return end
  local target = compat.piece_of(params.callback_owner or obj)
  if target == compat.Global or target == nil then target = game end
  if params.params ~= nil then target.call(name, p, params.params) else target.call(name, p) end
end
function compat.after_spawn(obj, p, params) wait.frames(1, function() compat.call_back(obj, p, params) end) end
function compat.put_object(obj, piece)
  if not compat.holds(obj) then return nil end
  local target = compat.piece_of(piece)
  if type(target) ~= "table" or target.id == nil then error(clr("LuaObject", piece), 0) end
  obj.stow(target)
  return obj
end
function compat.get_objects(obj)
  if not compat.holds(obj) then
    print("getObjects: Attempting to call getObjects() on an object that does not support getObjects()")
    return nil
  end
  local t = {}
  for i, s in ipairs(obj.stowed or {}) do
    t[#t + 1] = { name = s.name, nickname = s.name, description = "", gm_notes = "", guid = s.guid, lua_script = "", lua_script_state = "", memo = "", tags = {}, index = i - 1 }
  end
  return t
end
function compat.get_quantity(obj)
  if not compat.holds(obj) then return -1 end
  return #(obj.stowed or {})
end

function compat.save_answer(...)
  if select("#", ...) == 0 then return nil end
  local r = ...
  if r == nil or r ~= r then return nil end
  if type(r) == "string" then return r end
  return ""
end
function compat.try_answer(...)
  if select("#", ...) == 0 then return false end
  local r = ...
  if type(r) == "boolean" then return r end
  if type(r) == "number" then return r == r and r ~= 0 end
  return false
end

function compat.spawned(piece, p)
  compat.set_prop(piece, "spawning", true)
  wait.frames(1, function()
    compat.set_prop(piece, "spawning", false)
    compat.call_back(compat.Global, piece, p)
  end)
end

function compat.start_coroutine(owner, name)
  name = tostring(name)
  local target = compat.piece_of(owner)
  local f
  if target == nil or target == compat.Global then f = game.get_var(name) else f = target.get_var(name) end
  if type(f) ~= "function" then return true end
  local co = coroutine.create(f)
  local function step()
    local ok, v = coroutine.resume(co)
    if not ok then
      print("startLuaCoroutine/" .. name .. ": " .. tostring(v))
      return
    end
    if type(v) ~= "number" or v == 0 then wait.frames(1, step) end
  end
  step()
  return true
end

local global_lines_set = false
local set_global_lines_before = compat.set_global_vector_lines
local global_lines_before = compat.global_vector_lines
function compat.set_global_vector_lines(lines)
  global_lines_set = true
  set_global_lines_before(lines)
  return true
end
function compat.global_vector_lines()
  if not global_lines_set then return nil end
  return global_lines_before()
end

local notes_before = compat.Notes
local tab_colors = {}
local function tab_cards() return board.pieces({ tag = "notebook" }) or {} end
compat.Notes = {
  getNotes = notes_before.getNotes,
  setNotes = function(s)
    if s == nil then error("Error: missing parameter or parameter was null for setNotes.", 0) end
    return notes_before.setNotes(s)
  end,
  getNotebookTabs = function()
    local list = notes_before.getNotebookTabs()
    local cards = tab_cards()
    table.sort(cards, function(a, b) return a.id < b.id end)
    for i, t in ipairs(list) do t.color = cards[i] and tab_colors[cards[i].id] or "Grey" end
    return list
  end,
  addNotebookTab = function(p)
    p = type(p) == "table" and p or {}
    if p.title == nil or tostring(p.title) == "" then return -1 end
    if notes_before.addNotebookTab({ title = p.title, body = p.body or "" }) < 0 then return -1 end
    local cards = tab_cards()
    table.sort(cards, function(a, b) return a.id < b.id end)
    if p.color ~= nil and cards[#cards] then tab_colors[cards[#cards].id] = LABELS[p.color] and p.color or "White" end
    return #cards
  end,
  editNotebookTab = function(p)
    p = type(p) == "table" and p or {}
    local ok = notes_before.editNotebookTab(p)
    if ok and p.color ~= nil then
      local cards = tab_cards()
      table.sort(cards, function(a, b) return a.id < b.id end)
      local c = cards[(tonumber(p.index) or -1) + 1]
      if c then tab_colors[c.id] = LABELS[p.color] and p.color or "White" end
    end
    return ok
  end,
  removeNotebookTab = notes_before.removeNotebookTab,
}

function compat.add_attachment(obj, child)
  local c = compat.piece_of(child)
  if type(c) ~= "table" or c.id == nil then error(clr("LuaObject", child), 0) end
  obj.attach(c)
  return true
end
function compat.get_attachments(obj)
  local t = {}
  for i, a in ipairs(obj.attached or {}) do
    t[i] = { name = compat.name(a), nickname = compat.name(a), description = compat.description(a), gm_notes = compat.get_gm_notes(a), guid = compat.guid(a),
      lua_script = a.script or "", lua_script_state = a.script_state or "", memo = a.memo or "", tags = compat.tags(a), index = i - 1 }
  end
  return t
end
function compat.remove_attachments(obj)
  local out = {}
  for _, a in ipairs(obj.attached or {}) do
    a.detach()
    out[#out + 1] = a
  end
  return out
end

function compat.container(obj)
  if not compat.holds(obj) then return nil end
  return {
    __container = true,
    putObject = function() return nil end, takeObject = function() return nil end, getData = function() return nil end,
    setData = function() return true end, addData = function() return true end, removeData = function() return nil end,
    getInfo = function() return nil end, reset = function() return true end, randomize = function() return true end, getCount = function() return 0 end,
    search = function(color) return compat.who_at(color) ~= nil end,
  }
end

for _, pair in ipairs({ { "get_data", "getData" }, { "randomize", "randomize" }, { "put_object", "putObject" }, { "take_object", "takeObject" } }) do
  local before = compat[pair[1]]
  compat[pair[1]] = function(obj, ...)
    if type(obj) == "table" and rawget(obj, "__container") then return obj[pair[2]](...) end
    return before(obj, ...)
  end
end
local todo_before = compat.todo
function compat.todo(obj, name, ...)
  if type(obj) == "table" and rawget(obj, "__container") and type(obj[name]) == "function" then return obj[name](...) end
  return todo_before(obj, name, ...)
end

function compat.held_by_color(obj)
  local who = obj.held_by
  if who == nil then return nil end
  return compat.color_of(who)
end

local function kind_type(obj)
  local src = obj.source
  if src then return src.type end
  for _, t in ipairs(obj.tags or {}) do
    if t == "model" then return "Custom_Model" end
    if t == "token" then return "Custom_Token" end
  end
  return "Custom_Tile"
end
function compat.object_type(obj, member)
  if not compat.is_piece(obj) then
    if type(obj) == "table" then return obj[member or "name"] end
    return nil
  end
  return kind_type(compat.piece_of(obj))
end
local MODEL_TYPES = { Custom_Model = true, Custom_Model_Bag = true, Custom_Model_Infinite_Bag = true }
local MESH_TAGS = { "Generic", "Figurine", "Dice", "Coin", "Board", "Chip", "Bag", "Infinite" }
local NAME_TAGS = {
  Custom_Tile = "Tile", Custom_Token = "Tile", Custom_Board = "Board", Card = "Card", CardCustom = "Card", Deck = "Deck", DeckCustom = "Deck",
  Bag = "Bag", Infinite_Bag = "Infinite", Custom_Dice = "Dice", Custom_Figurine = "Figurine", Notecard = "Notecard", ["3DText"] = "3D-Text",
}
local function kind_tag(obj)
  local src = obj.source
  if src and src.tag then return src.tag end
  local t = kind_type(obj)
  if NAME_TAGS[t] then return NAME_TAGS[t] end
  if t:match("^Die_") then return "Dice" end
  if t:match("^Figurine_") then return "Figurine" end
  return "Generic"
end
function compat.object_tag(obj, member)
  if not compat.is_piece(obj) then
    if type(obj) == "table" then return obj[member or "type"] end
    return nil
  end
  return kind_tag(compat.piece_of(obj))
end
local function mesh_index(obj)
  local tag = kind_tag(obj)
  for i, m in ipairs(MESH_TAGS) do
    if m == tag then return i - 1 end
  end
  return 0
end
function compat.get_custom_object(obj)
  local src = obj.source or {}
  local links = src.links or {}
  local t = kind_type(obj)
  if MODEL_TYPES[t] then
    return { mesh = links.mesh or obj.def, diffuse = links.diffuse or "", normal = links.normal or "", collider = "", convex = true, type = mesh_index(obj), material = 0,
      cast_shadows = true, specular_intensity = 0.1, specular_color = compat.Color(1, 1, 1, 1), specular_sharpness = 3, fresnel_strength = 0.1 }
  end
  if t == "Custom_Assetbundle" then return { assetbundle = links.assetbundle or obj.def, assetbundle_secondary = "", type = mesh_index(obj), material = 0 } end
  local c = { image = links.image or obj.def, image_bottom = links.image_secondary or "", image_secondary = links.image_secondary or "", image_scalar = 1 }
  if t == "Custom_Token" then
    c.thickness, c.merge_distance, c.stand_up, c.stackable = (src.top or 0.1) * 2, 15, src.tag == "Figurine", false
  elseif t == "Custom_Tile" then
    c.type, c.thickness, c.stackable = 0, (src.top or 0.1) * 2, false
  end
  return c
end
local get_data_before = compat.get_data
function compat.get_data(obj, state)
  local d = get_data_before(obj, state)
  if type(d) ~= "table" or not compat.is_piece(obj) then return d end
  local t = kind_type(compat.piece_of(obj))
  d.Name = t
  local c = compat.get_custom_object(compat.piece_of(obj))
  if MODEL_TYPES[t] then
    d.CustomMesh = { MeshURL = c.mesh, DiffuseURL = c.diffuse, NormalURL = c.normal, ColliderURL = "", Convex = true, MaterialIndex = 0, TypeIndex = c.type, CastShadows = true }
  elseif t == "Custom_Token" or t == "Custom_Tile" then
    d.CustomImage = { ImageURL = c.image, ImageSecondaryURL = c.image_secondary, ImageScalar = 1, WidthScale = 0 }
  end
  return d
end

function compat.none() return nil end
function compat.no() return false end

local menu_n = 0
local function menus_of(obj)
  local b = compat.__books[obj.id]
  if b == nil then
    b = { buttons = {}, inputs = {} }
    compat.__books[obj.id] = b
  end
  b.menus = b.menus or {}
  return b.menus
end
function compat.add_context_menu_item(env, obj, label, fn, keep_open)
  label = tostring(label)
  if type(fn) ~= "function" then
    print("addContextMenuItem: Function required for context menu (label = \"" .. label .. "\", object = " .. tostring(compat.name(obj)) .. ")")
    return false
  end
  local menus = menus_of(obj)
  menus[label] = menus[label] or {}
  if menus[label][fn] then return true end
  menu_n = menu_n + 1
  local name = "__menu_" .. menu_n
  menus[label][fn] = name
  env[name] = function(who) return fn(compat.color_of(who), compat.position(obj), obj) end
  obj.menu_add(label, name)
  return true
end
function compat.clear_context_menu(obj)
  obj.menu_clear()
  local b = compat.__books[obj.id]
  if b then b.menus = {} end
  return true
end
local global_menus = {}
function compat.add_global_context_menu_item(env, label, fn, keep_open)
  label = tostring(label)
  if type(fn) ~= "function" then
    print("addContextMenuItem: Function required for context menu (label = \"" .. label .. "\")")
    return false
  end
  global_menus[label] = global_menus[label] or {}
  if global_menus[label][fn] then return true end
  global_menus[label][fn] = true
  game.menu_add(label, function(who, pos) return fn(compat.color_of(who), compat.Vector(pos.x or 0, 0, pos.z or 0)) end)
  return true
end
function compat.clear_global_context_menu()
  game.menu_clear()
  global_menus = {}
  return true
end

local function axis_units(obj)
  local x, z = compat.local_units(obj)
  local k = obj.scale or 1
  return x * k, x * k, z * k
end
function compat.scale3(obj)
  local x, y, z = axis_units(obj)
  return compat.Vector(x, y, z)
end
function compat.set_scale(obj, v)
  local s = compat.vector_arg(v)
  local x = compat.local_units(obj)
  if x > 0 then obj.set_scale(s.x / x) end
  return true
end
function compat.scale_by(obj, k)
  local by = type(k) == "number" and compat.Vector(k, k, k) or compat.vector_arg(k)
  obj.set_scale((obj.scale or 1) * by.x)
  return true
end
function compat.position_to_world(obj, v)
  local p = compat.vector_arg(v)
  local ux, uy, uz = axis_units(obj)
  local x, y = p.x, p.y
  if obj.face_down then x, y = -x, -y end
  local w = obj.to_world(-x * ux, p.z * uz)
  return compat.Vector(w.x, (obj.y or 0) + y * uy, w.z)
end
function compat.position_to_local(obj, v)
  local p = compat.vector_arg(v)
  local ux, uy, uz = axis_units(obj)
  local l = obj.to_local(p.x, p.z)
  local x, y = -l.x / ux, (p.y - (obj.y or 0)) / uy
  if obj.face_down then x, y = -x, -y end
  return compat.Vector(x, y, l.z / uz)
end
local THICK = 0.2
function compat.get_bounds(obj)
  local s = obj.size
  local a = math.rad(obj.rot or 0)
  local c, si = math.abs(math.cos(a)), math.abs(math.sin(a))
  local t = THICK * (obj.scale or 1)
  return { center = compat.position(obj), size = compat.Vector(s.w * c + s.h * si, t, s.w * si + s.h * c), offset = compat.Vector(0, 0, 0) }
end
function compat.get_bounds_normalized(obj)
  local s = obj.size
  return { center = compat.position(obj), size = compat.Vector(s.w, THICK * (obj.scale or 1), s.h), offset = compat.Vector(0, 0, 0) }
end

function compat.get_vector_lines(obj)
  local given = compat.get_prop(obj, "__lines", nil)
  if given == nil then return nil end
  local out = {}
  for i, l in ipairs(given) do
    local pts = {}
    for k, p in ipairs(l.points) do pts[k] = compat.Vector(p.x, p.y, p.z) end
    out[i] = { points = pts, color = compat.Color(l.color.r, l.color.g, l.color.b, l.color.a), thickness = l.thickness,
      rotation = compat.Vector(l.rotation.x, l.rotation.y, l.rotation.z), loop = l.loop, square = l.square, steam_id = 0 }
  end
  return out
end
function compat.set_vector_lines(obj, lines)
  local kept, ours = {}, {}
  local ux, _, uz = axis_units(obj)
  for _, l in ipairs(lines or {}) do
    if type(l) ~= "table" then error(clr("LuaVectorLine", l), 0) end
    local rot = l.rotation ~= nil and compat.vector_arg(l.rotation) or compat.Vector(0, 0, 0)
    local col = l.color ~= nil and compat.color_arg(l.color) or compat.Color(1, 1, 1, 1)
    local line = { points = {}, color = col, thickness = tonumber(l.thickness) or 0.1, rotation = rot, loop = l.loop == true, square = l.square == true }
    local a = math.rad(rot.y)
    local c, s = math.cos(a), math.sin(a)
    local pts = {}
    for k, p in ipairs(l.points or {}) do
      local v = compat.vector_arg(p)
      line.points[k] = v
      local x, z = v.x * c + v.z * s, v.z * c - v.x * s
      pts[#pts + 1] = { x = -x * ux, z = z * uz }
    end
    kept[#kept + 1] = line
    if #pts >= 2 then ours[#ours + 1] = { points = pts, color = { r = col.r, g = col.g, b = col.b, a = col.a }, width = line.thickness, loop = line.loop } end
  end
  compat.set_prop(obj, "__lines", kept)
  obj.set_lines(ours)
  return true
end

local function decal_check(d, what)
  if type(d) ~= "table" then error(clr("LuaDecal", d), 0) end
  if d.url == nil or d.url == "" then print(what .. ": url was null or an empty string") return false end
  if d.name == nil or d.name == "" then print(what .. ": name was null or an empty string") return false end
  return true
end
local set_decals_before, add_decal_before = compat.set_decals, compat.add_decal
function compat.set_decals(obj, list)
  if list == nil then print("SetDecals: Decal table is empty!") return false end
  for _, d in ipairs(list) do if not decal_check(d, "SetDecals") then return false end end
  set_decals_before(obj, list)
  return true
end
function compat.add_decal(obj, d)
  if not decal_check(d, "AddDecal") then return false end
  add_decal_before(obj, d)
  return true
end
function compat.get_decals(obj)
  local list = compat.get_prop(obj, "decals", nil)
  if list == nil then return nil end
  local out = {}
  for i, d in ipairs(list) do
    out[i] = { name = d.name, url = d.url, position = compat.optional_vector_arg(d.position) or compat.Vector(0, 0, 0),
      rotation = compat.optional_vector_arg(d.rotation) or compat.Vector(0, 0, 0), scale = compat.optional_vector_arg(d.scale) or compat.Vector(1, 1, 1) }
  end
  return out
end

function compat.set_snap_points(obj, list)
  local kept = {}
  for i, sp in ipairs(list or {}) do
    if type(sp) ~= "table" then error(clr("LuaSnapPointParameters", sp), 0) end
    kept[i] = { position = compat.optional_vector_arg(sp.position) or compat.Vector(0, 0, 0), rotation = compat.optional_vector_arg(sp.rotation) or compat.Vector(0, 0, 0),
      rotation_snap = sp.rotation_snap == true, tags = sp.tags or {} }
  end
  compat.set_prop(obj, "snap", kept)
  return true
end
function compat.get_snap_points(obj)
  local out = {}
  for i, sp in ipairs(compat.get_prop(obj, "snap", nil) or {}) do
    out[i] = { position = sp.position, rotation = sp.rotation_snap and sp.rotation or compat.Vector(0, 0, 0), rotation_snap = sp.rotation_snap, tags = sp.tags }
  end
  return out
end

function compat.select(obj, color, on)
  local who = compat.player_target(color)
  if who == nil then return false end
  local now = false
  for _, p in ipairs(game.selected(who) or {}) do if p.id == obj.id then now = true end end
  game.select(who, obj, on)
  return now ~= on
end

local function message(v)
  if v == nil then return "" end
  if type(v) == "string" or type(v) == "number" then return tostring(v) end
  error(clr("System.String", v), 0)
end
local function say_color(c)
  local col = compat.color_arg(c, true)
  if col == nil then return { r = 1, g = 1, b = 1, a = 1 } end
  return { r = col.r, g = col.g, b = col.b, a = col.a }
end
function compat.print_to_all(msg, c, broadcast)
  game.say(message(msg), { color = say_color(c) })
  return true
end
function compat.print_to_color(msg, color, c, what)
  local who = compat.player_target(color)
  if who == nil then error("Error in " .. what .. ": Player " .. tostring(color) .. " does not exist.", 0) end
  game.say(message(msg), { to = who, color = say_color(c) })
  return true
end
function compat.seated_players()
  local t = {}
  for _, u in ipairs(game.umpires()) do
    local c = compat.color_of(u)
    if c ~= "Black" and c ~= "Grey" then t[#t + 1] = c end
  end
  return t
end
function compat.color_to_player_name(color)
  local who = compat.who_at(color)
  if who == nil then error("Error in ColorToPlayerName (probably color isn't seated/doesn't exist): no player", 0) end
  return who.name
end
function compat.get_player(color) return compat.player(color) end
function compat.destroy_object(obj)
  local p = compat.piece_of(obj)
  if type(p) ~= "table" or p.id == nil then error(clr("LuaObject", obj), 0) end
  p.delete()
  return true
end
function compat.clear_hotkeys() game.hotkeys_clear() return true end
function compat.clear_global_context_menu() game.menu_clear() return true end
function compat.add_hotkey(env, label, fn, on_up)
  if type(fn) ~= "function" then error(clr("MoonSharp.Interpreter.Closure", fn), 0) end
  game.hotkey(tostring(label), function(who, piece, pos, up) return fn(compat.color_of(who), piece, compat.Vector(pos.x or 0, 0, pos.z or 0), up) end, on_up and true or false)
  return true
end
function compat.add_global_context_menu_item(env, label, fn, keep_open)
  if type(fn) ~= "function" then
    print("addContextMenuItem: Function required for context menu (label = \"" .. tostring(label) .. "\")")
    return false
  end
  game.menu_add(tostring(label), function(who, pos) return fn(compat.color_of(who), compat.Vector(pos.x or 0, 0, pos.z or 0)) end)
  return true
end
function compat.erase_all()
  for _, s in ipairs(board.strokes()) do board.erase(s.id) end
  for _, p in ipairs(board.pieces()) do if #(p.lines or {}) > 0 then p.set_lines({}) end end
  return true
end
function compat.clear_pixel_paint() return false end

local LOG_PREFIX, LOG_DEPTH = "  ", 3
local OPEN_GUID, CLOSE_GUID = "〔", "〕"
local function float_text(n)
  if n == math.floor(n) and math.abs(n) < 1e15 then return string.format("%d", n) end
  return string.format("%.7g", n)
end
local fragment
local function table_text(t, prefix, sep)
  local parts = {}
  for k, v in pairs(t) do parts[#parts + 1] = prefix .. tostring(k) .. ": " .. fragment(v, prefix, sep) end
  return table.concat(parts, sep)
end
fragment = function(v, prefix, sep)
  if v == nil then return "nil" end
  if type(v) == "string" then return v end
  if type(v) == "boolean" then return tostring(v) end
  if type(v) == "number" then return float_text(v) end
  if type(v) == "function" then return "<func>" end
  if type(v) == "table" then
    if rawget(v, "__params") ~= nil or (type(v.id) == "string" and type(v.set_name) == "function") then
      local p = compat.piece_of(v)
      return "<LuaGameObjectScript>" .. OPEN_GUID .. tostring(compat.guid(p)) .. CLOSE_GUID .. tostring(compat.name(p))
    end
    if #prefix < #LOG_PREFIX * LOG_DEPTH then return sep .. table_text(v, prefix .. LOG_PREFIX, "\n") end
    return "<table>"
  end
  return "<" .. type(v) .. ">"
end
function compat.log_text(value, label)
  local text = fragment(value, "", "")
  if label ~= nil and tostring(label) ~= "" then text = tostring(label) .. "\n" .. text end
  return text
end
function compat.log(value, label, tags)
  print(compat.log_text(value, label))
  return true
end
function compat.log_string(value, label, tags) return compat.log_text(value, label) end
function compat.debug_log(msg)
  print(message(msg))
  return true
end

local GM_NOTES = "gm_notes"
function compat.set_gm_notes(obj, s)
  if type(obj.set_gm_notes) == "function" then obj.set_gm_notes(s) else compat.set_prop(obj, GM_NOTES, s) end
  return true
end
function compat.get_gm_notes(obj)
  local own = compat.get_prop(obj, GM_NOTES, nil)
  if own ~= nil then return own end
  return obj.gm_notes or ""
end
local lit = {}
function compat.highlight(obj, color, seconds)
  local c = compat.color_arg(color)
  local p = compat.piece_of(obj)
  local round = (lit[p.id] or 0) + 1
  lit[p.id] = round
  p.set_highlight({ r = c.r, g = c.g, b = c.b, a = c.a })
  local s = tonumber(seconds)
  if s ~= nil then
    wait.seconds(s, function()
      if lit[p.id] == round and p.exists ~= false then p.set_highlight(nil) end
    end)
  end
  return true
end
function compat.highlight_off(obj)
  local p = compat.piece_of(obj)
  lit[p.id] = (lit[p.id] or 0) + 1
  p.set_highlight(nil)
  return true
end
local live = {}
local function closure(fn, what)
  if fn == nil then error("Error in Wait." .. what .. ": function is nil", 0) end
  if type(fn) ~= "function" then error(clr("MoonSharp.Interpreter.Closure", fn), 0) end
end
local function guarded(fn)
  local ok, err = pcall(fn)
  if not ok then print("Wait: " .. tostring(err)) end
end
function compat.wait_frames(fn, n)
  closure(fn, "frames")
  local id
  id = wait.frames(math.floor(tonumber(n) or 1), function()
    live[id] = nil
    guarded(fn)
  end)
  live[id] = id
  return id
end
function compat.wait_time(fn, seconds, repeats)
  closure(fn, "time")
  repeats = math.floor(tonumber(repeats) or 1)
  if repeats == -1 then repeats = math.huge end
  seconds = tonumber(seconds) or 0
  local id
  local target = wait.now() + seconds
  local function once()
    if repeats > 1 then
      repeats = repeats - 1
      target = target + seconds
      live[id] = wait.seconds(math.max(0, target - wait.now()), once)
    else
      live[id] = nil
    end
    guarded(fn)
  end
  id = wait.seconds(seconds, once)
  live[id] = id
  return id
end
function compat.wait_condition(fn, cond, timeout, timeout_fn)
  closure(fn, "condition")
  if cond == nil then error("Error in Wait.condition: condition_function is nil", 0) end
  if type(cond) ~= "function" then error(clr("MoonSharp.Interpreter.Closure", cond), 0) end
  local ok, first = pcall(cond)
  if not ok or type(first) ~= "boolean" then error("Error in Wait.condition: condition_function does not return bool", 0) end
  local function asked()
    local fine, v = pcall(cond)
    if not fine then
      print("Wait: " .. tostring(v))
      return true
    end
    return v == true
  end
  local id
  local fire = function()
    live[id] = nil
    guarded(fn)
  end
  local lapse = function()
    live[id] = nil
    if type(timeout_fn) == "function" then guarded(timeout_fn) end
  end
  id = wait.condition(asked, fire, tonumber(timeout), lapse)
  live[id] = id
  return id
end
function compat.wait_stop(id)
  local n = tonumber(id)
  if n == nil then return false end
  local now = live[n]
  if now == nil then return false end
  live[n] = nil
  return wait.stop(now)
end
function compat.wait_stop_all()
  local ids = {}
  for id in pairs(live) do ids[#ids + 1] = id end
  for _, id in ipairs(ids) do compat.wait_stop(id) end
end

local timers = {}
local function timer_field(name)
  error("cannot access field " .. tostring(name) .. " of userdata<LuaTimer>", 0)
end
function compat.timer_create(env, p) return compat.Timer.create(p, env) end
compat.Timer = setmetatable({
  create = function(p, env)
    if type(p) ~= "table" then error(clr("MoonSharp.Interpreter.Table", p), 0) end
    local name = p.identifier ~= nil and tostring(p.identifier) or ""
    if name == "" then return false end
    local fn_name = p.function_name ~= nil and tostring(p.function_name) or ""
    if fn_name == "" then return false end
    if timers[name] ~= nil then
      print("Timer already contains identifier: " .. name)
      return false
    end
    local owner = p.function_owner
    local args = p.parameters ~= nil and compat.copy_lua_table(p.parameters) or nil
    local reps = math.floor(tonumber(p.repetitions) or 1)
    if reps == 0 then reps = -1 end
    local delay = tonumber(p.delay) or 0
    local function fire()
      if reps > 0 then reps = reps - 1 end
      if reps == 0 then timers[name] = nil else timers[name] = wait.seconds(delay, fire) end
      local target = owner ~= nil and compat.piece_of(owner) or nil
      local ok, err
      if target ~= nil and target ~= compat.Global then ok, err = pcall(target.call, fn_name, args)
      elseif owner == compat.Global then ok, err = pcall(game.call, fn_name, args)
      else
        local f = type(env) == "table" and env[fn_name] or nil
        if type(f) ~= "function" then return end
        ok, err = pcall(f, args)
      end
      if not ok then print("Error calling Lua function, " .. fn_name .. ", in Timer: " .. tostring(err)) end
    end
    timers[name] = wait.seconds(delay, fire)
    return true
  end,
  destroy = function(name)
    local h = timers[tostring(name)]
    if h == nil then return false end
    wait.stop(h)
    timers[tostring(name)] = nil
    return true
  end,
}, { __index = function(_, k) timer_field(k) end })

local FIXED_STEP = 0.02
compat.Time = setmetatable({}, { __index = function(_, k)
  if k == "time" then return wait.now() end
  if k == "frame_count" then return wait.frame() end
  if k == "delta_time" then return wait.delta() end
  if k == "fixed_delta_time" then return FIXED_STEP end
  if k == "fixed_time" then return math.floor(wait.now() / FIXED_STEP) * FIXED_STEP end
  error("cannot access field " .. tostring(k) .. " of userdata<LuaTime>", 0)
end })

return compat
