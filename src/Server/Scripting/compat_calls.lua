local compat = compat

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

function compat.rotation(obj) return compat.Vector(0, turn(obj.rot or 0), obj.face_down and HALF_TURN or 0) end

local function lie(obj, r)
  local x, y, z = math.rad(r.x), r.y, math.rad(r.z)
  local down = math.cos(x) * math.cos(z) < 0
  local heading = y
  if math.cos(x) < 0 then heading = y + HALF_TURN end
  if down ~= (obj.face_down == true) then obj.flip(down) end
  obj.set_rot(turn(heading))
end

function compat.set_rotation(obj, v) lie(obj, compat.vector_arg(v)) return true end
function compat.rotate(obj, v)
  local by = compat.vector_arg(v)
  local now = compat.rotation(obj)
  lie(obj, compat.Vector(now.x + by.x, now.y + by.y, now.z + by.z))
  return true
end
function compat.set_position(obj, v)
  local p = compat.vector_arg(v)
  obj.set_pos(p.x, p.z)
  return true
end
function compat.translate(obj, v)
  local by = compat.vector_arg(v)
  obj.set_pos(obj.pos.x + by.x, obj.pos.z + by.z)
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
function compat.reload(obj)
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

function compat.take_object(obj, params)
  params = type(params) == "table" and params or {}
  local position = compat.optional_vector_arg(params.position)
  local rotation = compat.optional_vector_arg(params.rotation)
  if not obj.holds then return nil end
  local here = compat.position(obj)
  position = position or compat.Vector(here.x + TAKE_OFFSET, here.y, here.z)
  rotation = rotation or compat.rotation(obj)
  local which = params.guid
  if which == nil and type(params.index) == "number" then which = math.floor(params.index) + 1 end
  local p = obj.unstow(which, { x = position.x, z = position.z }, turn(rotation.y))
  if p == nil then return nil end
  if (math.cos(math.rad(rotation.x)) * math.cos(math.rad(rotation.z)) < 0) ~= (p.face_down == true) then p.flip() end
  compat.after_spawn(obj, p, params)
  return p
end
function compat.after_spawn(obj, p, params)
  local cb = params.callback_function
  if cb ~= nil and type(cb) ~= "function" then
    print("Error converting Lua Table entry callback_function to Type MoonSharp.Interpreter.Closure. Value = " .. tostring(cb))
    cb = nil
  end
  if cb ~= nil then
    wait.frames(1, function() cb(p) end)
    return
  end
  local name = params.callback
  if type(name) ~= "string" or name == "" then return end
  local owner = params.callback_owner or obj
  local extra = params.params
  wait.frames(1, function()
    local target = compat.piece_of(owner)
    if target == compat.Global then target = game end
    if extra ~= nil then target.call(name, p, extra) else target.call(name, p) end
  end)
end
function compat.put_object(obj, piece)
  if not obj.holds then return nil end
  local target = compat.piece_of(piece)
  if type(target) ~= "table" or target.id == nil then error(clr("LuaObject", piece), 0) end
  obj.stow(target)
  return obj
end
function compat.get_objects(obj)
  if not obj.holds then
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
  if not obj.holds then return -1 end
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
function compat.set_gm_notes(obj, s) compat.set_prop(obj, GM_NOTES, s) return true end
function compat.get_gm_notes(obj) return compat.get_prop(obj, GM_NOTES, "") end
function compat.highlight(obj, color, seconds)
  compat.color_arg(color)
  return true
end
function compat.highlight_off(obj) return true end
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
