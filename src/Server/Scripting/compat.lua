local compat = {}

local function num(v, d) if type(v) == "number" then return v end return d end
local function vec(v, k, i, d)
  if type(v) ~= "table" then return d end
  local x = v[k]
  if x == nil then x = v[i] end
  return num(x, d)
end
local function has(list, v) for _, x in ipairs(list or {}) do if x == v then return true end end return false end
local function unit(obj)
  local s = obj.size
  if not s then return 0.5 end
  local k = obj.scale
  if type(k) ~= "number" or k <= 0 then k = 1 end
  local w, h = s.w / k, s.h / k
  if has(obj.tags, "token") then return math.max(w, h) / 3 end
  return h / 2
end

local TTS_COLORS = { "White", "Red", "Blue", "Green", "Yellow", "Orange", "Purple", "Pink", "Brown", "Teal", "Grey", "Black" }
local function umpires() return game.umpires() end

function compat.color(c)
  if type(c) == "string" then return c end
  if type(c) == "table" then return { vec(c, "r", 1, 0), vec(c, "g", 2, 0), vec(c, "b", 3, 0), vec(c, "a", 4, 1) } end
  return nil
end

function compat.color_of(who)
  if type(who) ~= "table" then return "White" end
  if who.role == "host" then return "White" end
  local i = 2
  for _, u in ipairs(umpires()) do
    if u.role ~= "host" then
      if u.id == who.id then return TTS_COLORS[math.min(i, #TTS_COLORS)] end
      i = i + 1
    end
  end
  return "Grey"
end

function compat.who_at(color)
  for _, u in ipairs(umpires()) do
    if compat.color_of(u) == color then return u end
  end
  return nil
end

function compat.player(color)
  local who = compat.who_at(color)
  return {
    color = color, seated = who ~= nil,
    steam_name = who and who.name or color, steam_id = who and tostring(who.id) or "", admin = who ~= nil and who.role ~= "umpire", host = who ~= nil and who.role == "host",
    broadcast = function(msg, c) game.say(tostring(msg), { to = who, color = compat.color(c) }) end,
    print = function(msg, c) game.say(tostring(msg), { to = who, color = compat.color(c) }) end,
    getSelectedObjects = function() return who and game.selected(who) or {} end,
    getHoverObject = function() return who and game.hovered(who) or nil end,
    getHandObjects = function() return {} end,
    getHandCount = function() return 0 end,
    getHandTransform = function() return { position = { x = 0, y = 0, z = 0 }, rotation = { x = 0, y = 0, z = 0 }, scale = { x = 1, y = 1, z = 1 } } end,
    setHandTransform = function() end,
    lookAt = function() end, pingTable = function() end,
    showInfoDialog = function(msg) game.say(tostring(msg), { to = who }) end,
    showConfirmDialog = function(msg, fn) return game.dialog(who, { kind = "confirm", title = "", text = tostring(msg or "") }, function(v, w) if v ~= nil and fn then fn(compat.color_of(w)) end end) end,
    showInputDialog = function(desc, default, fn) return game.dialog(who, { kind = "input", title = tostring(desc or ""), text = tostring(default or "") }, function(v, w) if v ~= nil and fn then fn(v, compat.color_of(w)) end end) end,
    showMemoDialog = function(desc, default, fn) return game.dialog(who, { kind = "memo", title = tostring(desc or ""), text = tostring(default or "") }, function(v, w) if v ~= nil and fn then fn(v, compat.color_of(w)) end end) end,
    showOptionsDialog = function(desc, options, default, fn) return game.dialog(who, { kind = "options", title = tostring(desc or ""), text = tostring(default or ""), options = options or {} }, function(v, w) if v ~= nil and fn then local i = 0; for k, o in ipairs(options or {}) do if o == v then i = k end end; fn(v, i, compat.color_of(w)) end end) end,
    showColorDialog = function(default, fn) return game.dialog(who, { kind = "color", title = "Colour", text = tostring(default or "") }, function(v, w) if v ~= nil and fn then fn(compat.string_color_to_rgb(v), compat.color_of(w)) end end) end,
    clearSelectedObjects = function() end, kick = function() end, mute = function() end, changeColor = function() end,
  }
end

compat.Player = setmetatable({
  getPlayers = function()
    local t = {}
    for _, u in ipairs(umpires()) do t[#t + 1] = compat.player(compat.color_of(u)) end
    return t
  end,
  getAvailableColors = function() return { "Red", "Blue", "Green", "Yellow", "Orange", "Purple", "Pink", "Brown", "Teal" } end,
  getColors = function() return TTS_COLORS end,
  getSpectators = function() return {} end,
}, { __index = function(_, color) return compat.player(color) end })

function compat.seated_players()
  local t = {}
  for _, u in ipairs(umpires()) do t[#t + 1] = compat.color_of(u) end
  return t
end

function compat.player_target(color)
  if type(color) == "table" and color.id then return color end
  return compat.who_at(color)
end

function compat.broadcast_to_color(msg, color, c)
  local who = compat.player_target(color)
  if who then game.say(tostring(msg), { to = who, color = compat.color(c) }) end
end

compat.Global = {
  call = function(fn, args) return game.call(fn, args) end,
  getVar = function(n) return game.get_var(n) end,
  setVar = function(n, v) game.set_var(n, v) end,
  getTable = function(n) return game.get_var(n) end,
  setTable = function(n, v) game.set_var(n, v) end,
}

local VectorMeta = {}
VectorMeta.__index = VectorMeta
function compat.Vector(x, y, z)
  if type(x) == "table" then return setmetatable({ x = vec(x, "x", 1, 0), y = vec(x, "y", 2, 0), z = vec(x, "z", 3, 0) }, VectorMeta) end
  return setmetatable({ x = num(x, 0), y = num(y, 0), z = num(z, 0) }, VectorMeta)
end
VectorMeta.__add = function(a, b) return compat.Vector(a.x + b.x, a.y + b.y, a.z + b.z) end
VectorMeta.__sub = function(a, b) return compat.Vector(a.x - b.x, a.y - b.y, a.z - b.z) end
VectorMeta.__mul = function(a, b) if type(b) == "number" then return compat.Vector(a.x * b, a.y * b, a.z * b) end return compat.Vector(a * b.x, a * b.y, a * b.z) end
VectorMeta.__unm = function(a) return compat.Vector(-a.x, -a.y, -a.z) end
VectorMeta.__eq = function(a, b) return a.x == b.x and a.y == b.y and a.z == b.z end
VectorMeta.__tostring = function(a) return string.format("(%g, %g, %g)", a.x, a.y, a.z) end
function VectorMeta:add(b) return self + b end
function VectorMeta:sub(b) return self - b end
function VectorMeta:scale(k) return self * k end
function VectorMeta:copy() return compat.Vector(self.x, self.y, self.z) end
function VectorMeta:magnitude() return math.sqrt(self.x * self.x + self.y * self.y + self.z * self.z) end
function VectorMeta:distance(b) return (self - b):magnitude() end
function VectorMeta:normalized() local m = self:magnitude(); if m == 0 then return self:copy() end return self * (1 / m) end
function VectorMeta:setAt(k, v) self[k] = v; return self end
function VectorMeta:set(x, y, z) self.x = num(x, self.x); self.y = num(y, self.y); self.z = num(z, self.z); return self end
function VectorMeta:dot(b) return self.x * b.x + self.y * b.y + self.z * b.z end
function VectorMeta:lerp(b, t) return self + (b - self) * t end
function VectorMeta:angle(b) local d = self:dot(b) / (self:magnitude() * b:magnitude()); return math.deg(math.acos(math.max(-1, math.min(1, d)))) end
function compat.Color(r, g, b, a) return { r = num(r, 0), g = num(g, 0), b = num(b, 0), a = num(a, 1) } end

function compat.guid(obj) return obj.guid or obj.id end
function compat.name(obj) return obj.name or "" end
function compat.description(obj) return obj.note or "" end
function compat.position(obj) local p = obj.pos; return compat.Vector(p.x, obj.y or 0, p.z) end
function compat.rotation(obj) return compat.Vector(0, obj.rot or 0, obj.face_down and 180 or 0) end
function compat.scale3(obj) local s = obj.scale or 1; return compat.Vector(s, s, s) end
function compat.set_position(obj, v) obj.set_pos(vec(v, "x", 1, obj.pos.x), vec(v, "z", 3, obj.pos.z)) end
function compat.set_rotation(obj, v) obj.set_rot(vec(v, "y", 2, obj.rot)) end
function compat.set_scale(obj, v) if type(v) == "number" then obj.set_scale(v) else obj.set_scale(vec(v, "x", 1, obj.scale)) end end
function compat.scale_by(obj, k) obj.set_scale((obj.scale or 1) * num(k, 1)) end
function compat.rotate(obj, v) obj.set_rot((obj.rot or 0) + vec(v, "y", 2, 0)) end
function compat.translate(obj, v) obj.set_pos(obj.pos.x + vec(v, "x", 1, 0), obj.pos.z + vec(v, "z", 3, 0)) end
function compat.position_to_world(obj, v)
  local u = unit(obj)
  local w = obj.to_world(vec(v, "x", 1, 0) * u, vec(v, "z", 3, 0) * u)
  return compat.Vector(w.x, vec(v, "y", 2, 0), w.z)
end
function compat.position_to_local(obj, v)
  local u = unit(obj)
  local l = obj.to_local(vec(v, "x", 1, 0), vec(v, "z", 3, 0))
  return compat.Vector(l.x / u, vec(v, "y", 2, 0), l.z / u)
end
function compat.transform_forward(obj) local a = math.rad(obj.rot or 0); return compat.Vector(math.sin(a), 0, math.cos(a)) end
function compat.transform_right(obj) local a = math.rad(obj.rot or 0); return compat.Vector(math.cos(a), 0, -math.sin(a)) end
function compat.transform_up(obj) return compat.Vector(0, 1, 0) end

local function hex2(s, i) return tonumber(s:sub(i, i + 1), 16) / 255 end
function compat.color_tint(obj)
  local t = obj.tint
  if type(t) == "string" and #t >= 7 and t:sub(1, 1) == "#" then return { r = hex2(t, 2), g = hex2(t, 4), b = hex2(t, 6), a = #t >= 9 and hex2(t, 8) or 1 } end
  return { r = 1, g = 1, b = 1, a = 1 }
end
function compat.tags(obj) return obj.tags or {} end
function compat.has_tag(obj, tag)
  for _, t in ipairs(obj.tags or {}) do if t == tag then return true end end
  return false
end
function compat.log_string(v, label)
  local function dump(x, indent, seen)
    if type(x) ~= "table" then return tostring(x) end
    if seen[x] then return "<cycle>" end
    seen[x] = true
    local parts = {}
    for k, val in pairs(x) do parts[#parts + 1] = indent .. "  " .. tostring(k) .. " = " .. dump(val, indent .. "  ", seen) end
    table.sort(parts)
    return "{\n" .. table.concat(parts, ",\n") .. "\n" .. indent .. "}"
  end
  return (label ~= nil and label ~= "" and (tostring(label) .. ": ") or "") .. dump(v, "", {})
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
function compat.states(obj)
  local t = {}
  for i, name in ipairs(obj.states or {}) do t[#t + 1] = { id = i, name = name, guid = obj.guid, nickname = name, description = "", lua_script = "", lua_script_state = "" } end
  return t
end
function compat.get_script(obj) return obj.script or "" end
function compat.set_lua_script(obj, s) obj.set_script(s) end

local props = {}
compat.__props = props
local function is_piece(obj)
  return type(obj) == "table" and (rawget(obj, "__params") ~= nil or (type(obj.id) == "string" and type(obj.set_name) == "function"))
end
local function pbag(obj) local id = obj.id or obj; props[id] = props[id] or {}; return props[id] end
function compat.set_prop(obj, k, v)
  if not is_piece(obj) then if type(obj) == "table" then obj[k] = v end return end
  if k == "memo" then obj.set_memo(v) return end
  if k == "script_state" then obj.set_script_state(v) return end
  pbag(obj)[k] = v
end
function compat.get_prop(obj, k, d)
  if not is_piece(obj) then if type(obj) == "table" and obj[k] ~= nil then return obj[k] end return d end
  if k == "memo" then local m = obj.memo; if m == nil or m == "" then return d end return m end
  if k == "script_state" then local v = obj.script_state; if v == nil or v == "" then return d end return v end
  local v = pbag(obj)[k]; if v == nil then return d end return v
end
function compat.set_lock(obj, v) obj.set_locked(v and true or false) end
function compat.get_lock(obj) return obj.locked == true end
function compat.set_memo(obj, s) obj.set_memo(s) end
function compat.get_memo(obj) return obj.memo end
function compat.set_gm_notes(obj, s) pbag(obj).gm_notes = s end
function compat.get_gm_notes(obj) return pbag(obj).gm_notes or "" end
function compat.set_interactable(obj, v) pbag(obj).interactable = v end
function compat.highlight(obj, color, seconds) end
function compat.highlight_off(obj) end
function compat.set_vector_lines(obj, lines)
  pbag(obj).lines = lines
  local u = unit(obj)
  local ours = {}
  for _, l in ipairs(lines or {}) do
    local pts = {}
    for _, p in ipairs(l.points or {}) do pts[#pts + 1] = { x = vec(p, "x", 1, 0) * u, z = vec(p, "z", 3, 0) * u } end
    if #pts >= 2 then ours[#ours + 1] = { points = pts, color = compat.color(l.color), width = num(l.thickness, 0.1), loop = l.loop == true } end
  end
  obj.set_lines(ours)
end
function compat.get_vector_lines(obj) return pbag(obj).lines or {} end
local function our_decal(obj, d)
  local url = d and d.url
  if type(url) ~= "string" or url == "" then return nil end
  if url:find("://", 1, true) then compat.todo(obj, "a decal whose picture is only on the web") return nil end
  local u = unit(obj)
  local px, py, pz = vec(d.position, "x", 1, 0), vec(d.position, "y", 2, 0), vec(d.position, "z", 3, 0)
  local back = py < 0
  return {
    picture = url, name = tostring(d.name or ""), face = back and "back" or "front",
    x = (back and px or -px) * u, z = pz * u,
    w = math.abs(vec(d.scale, "x", 1, 1)) * u, h = math.abs(vec(d.scale, "y", 2, 1)) * u,
    rot = vec(d.rotation, "y", 2, 0) - 180,
  }
end
function compat.set_decals(obj, d)
  pbag(obj).decals = d or {}
  local ours = {}
  for _, dc in ipairs(d or {}) do ours[#ours + 1] = our_decal(obj, dc) end
  local ok, err = pcall(obj.set_decals, ours)
  if not ok then print("setDecals: " .. tostring(err)) end
  return ok
end
function compat.add_decal(obj, d)
  local all = {}
  for i, dc in ipairs(pbag(obj).decals or {}) do all[i] = dc end
  all[#all + 1] = d
  return compat.set_decals(obj, all)
end
function compat.get_decals(obj) return pbag(obj).decals or {} end
function compat.set_snap_points(obj, s) pbag(obj).snap = s end
function compat.get_snap_points(obj) return pbag(obj).snap or {} end
function compat.get_custom_object(obj) return { image = "", image_secondary = "", thickness = 0.2, type = 0 } end
function compat.get_data(obj, state)
  local p, r, names = compat.position(obj), compat.rotation(obj), obj.states or {}
  local tags = {}
  for i, t in ipairs(obj.tags or {}) do tags[i] = t end
  local d = {
    GUID = compat.guid(obj), Nickname = state and names[state] or compat.name(obj), Description = compat.description(obj),
    GMNotes = compat.get_gm_notes(obj), Tags = tags, Memo = obj.memo or "", LuaScript = compat.get_script(obj),
    LuaScriptState = obj.script_state or "", def = obj.def, State = state or obj.state or 1,
    Transform = { posX = p.x, posY = p.y, posZ = p.z, rotX = r.x, rotY = r.y, rotZ = r.z, scaleX = obj.scale or 1, scaleY = obj.scale or 1, scaleZ = obj.scale or 1 },
  }
  if not state and #names > 1 then
    d.States = {}
    for k = 1, #names do if k ~= d.State then d.States[k] = compat.get_data(obj, k) end end
  end
  return d
end
function compat.get_json(obj) return json.encode(compat.get_data(obj)) end
function compat.get_objects(obj) return {} end
function compat.get_quantity(obj) return -1 end
function compat.get_bounds(obj)
  local s = obj.size
  return { center = compat.position(obj), size = compat.Vector(s.w, 0.2, s.h), offset = compat.Vector(0, 0, 0) }
end
function compat.get_attachments(obj)
  local t = {}
  for i, a in ipairs(obj.attached or {}) do t[#t + 1] = { guid = compat.guid(a), name = compat.name(a), index = i - 1 } end
  return t
end
function compat.remove_attachment(obj, index)
  local a = (obj.attached or {})[(tonumber(index) or -1) + 1]
  if a == nil then return nil end
  a.detach()
  return a
end
function compat.remove_attachments(obj) for _, a in ipairs(obj.attached or {}) do a.detach() end end
function compat.is_destroyed(obj) return obj.exists == false end
function compat.get_selecting_players(obj) return {} end
function compat.select(obj, color, on)
  local who = compat.player_target(color)
  if who then game.select(who, obj, on) end
end
function compat.clone(obj, p)
  p = p or {}
  return compat.spawn_object_data({ data = compat.get_data(obj), position = p.position or compat.position(obj), rotation = p.rotation or compat.rotation(obj), callback_function = p.callback_function })
end

local books = {}
compat.__books = books
local function book(obj)
  local id = obj.id
  local b = books[id]
  if not b then b = { buttons = {}, inputs = {} }; books[id] = b end
  return b
end

local function handler(name, owner, obj)
  if name == nil or name == "" then return nil end
  name = tostring(name)
  if owner == compat.Global then return "global:" .. name end
  if is_piece(owner) and type(obj) == "table" then
    local oid = owner.id
    if type(oid) == "string" and oid ~= obj.id then return oid .. "/" .. name end
  end
  return name
end

local BUTTON_EM, INPUT_SCALE = 0.87, 2.2
local ALIGN = { [2] = "left", [3] = "center", [4] = "right" }

function compat.widget_spec(obj, t, kind)
  local u = unit(obj)
  local px, pz = vec(t.position, "x", 1, 0), vec(t.position, "z", 3, 0)
  local rx, ry, rz = vec(t.rotation, "x", 1, 0), vec(t.rotation, "y", 2, 0), vec(t.rotation, "z", 3, 0)
  local k = kind == "input" and INPUT_SCALE or 1
  local sx, sz = math.abs(vec(t.scale, "x", 1, 1)) * k, math.abs(vec(t.scale, "z", 3, 1)) * k
  local back = math.abs(((rz % 360) + 360) % 360 - 180) < 90 or math.abs(((rx % 360) + 360) % 360 - 180) < 90
  local s = {
    face = back and "back" or "front",
    x = (back and -px or px) * u, z = pz * u, rot = ry,
    w = num(t.width, 0) / 1000 * sx * u, h = num(t.height, 0) / 1000 * sz * u,
    font = num(t.font_size, 100) / 1000 * sx * u * BUTTON_EM,
    color = compat.color(t.color), text_color = compat.color(t.font_color), tooltip = t.tooltip,
    align = ALIGN[num(t.alignment, 3)] or "center",
  }
  if kind == "input" then
    s.value = tostring(t.value or ""); s.placeholder = tostring(t.label or "")
    s.on_change = handler(t.input_function, t.function_owner, obj)
  else
    s.label = tostring(t.label or "")
    s.on_click = handler(t.click_function, t.function_owner, obj)
  end
  return s
end

function compat.widget_patch(obj, t, kind)
  local p = {}
  local geo = t.position or t.rotation or t.scale or t.width or t.height or t.font_size
  if geo then
    local s = compat.widget_spec(obj, t, kind)
    if t.position then p.x = s.x; p.z = s.z end
    if t.rotation then p.face = s.face; p.rot = s.rot end
    if t.width then p.w = s.w end
    if t.height then p.h = s.h end
    if t.font_size then p.font = s.font end
  end
  if t.label ~= nil then if kind == "input" then p.placeholder = tostring(t.label) else p.label = tostring(t.label) end end
  if t.value ~= nil then p.value = tostring(t.value) end
  if t.color ~= nil then p.color = compat.color(t.color) end
  if t.font_color ~= nil then p.text_color = compat.color(t.font_color) end
  if t.tooltip ~= nil then p.tooltip = t.tooltip end
  if t.alignment ~= nil then p.align = ALIGN[num(t.alignment, 3)] or "center" end
  if t.click_function ~= nil then p.on_click = handler(t.click_function, t.function_owner, obj) end
  if t.input_function ~= nil then p.on_change = handler(t.input_function, t.function_owner, obj) end
  return p
end

local function rebuild(obj, b)
  obj.clear_widgets()
  for _, e in ipairs(b.buttons) do e.index = obj.button(e.spec) end
  for _, e in ipairs(b.inputs) do e.index = obj.input(e.spec) end
end

function compat.create_button(obj, t)
  local b = book(obj)
  local spec = compat.widget_spec(obj, t, "button")
  b.buttons[#b.buttons + 1] = { spec = spec, index = obj.button(spec), tts = t }
  return #b.buttons - 1
end
function compat.create_input(obj, t)
  local b = book(obj)
  local spec = compat.widget_spec(obj, t, "input")
  b.inputs[#b.inputs + 1] = { spec = spec, index = obj.input(spec), tts = t }
  return #b.inputs - 1
end
local function edit(obj, list, t, kind)
  local e = list[num(t.index, 0) + 1]
  if not e then return false end
  local patch = compat.widget_patch(obj, t, kind)
  for k, v in pairs(patch) do e.spec[k] = v end
  for k, v in pairs(t) do e.tts[k] = v end
  obj.edit(e.index, patch)
  return true
end
function compat.edit_button(obj, t) return edit(obj, book(obj).buttons, t, "button") end
function compat.edit_input(obj, t) return edit(obj, book(obj).inputs, t, "input") end
function compat.remove_button(obj, i) local b = book(obj); if b.buttons[num(i, 0) + 1] then table.remove(b.buttons, num(i, 0) + 1); rebuild(obj, b) end end
function compat.remove_input(obj, i) local b = book(obj); if b.inputs[num(i, 0) + 1] then table.remove(b.inputs, num(i, 0) + 1); rebuild(obj, b) end end
function compat.clear_buttons(obj) local b = book(obj); b.buttons = {}; rebuild(obj, b) end
function compat.clear_inputs(obj) local b = book(obj); b.inputs = {}; rebuild(obj, b) end
local function tts_list(list)
  local t = {}
  for i, e in ipairs(list) do local c = {}; for k, v in pairs(e.tts) do c[k] = v end; c.index = i - 1; t[#t + 1] = c end
  return t
end
function compat.get_buttons(obj) return tts_list(book(obj).buttons) end
function compat.get_inputs(obj)
  local t = tts_list(book(obj).inputs)
  for i, e in ipairs(book(obj).inputs) do
    for _, w in ipairs(obj.widgets()) do if w.kind == "input" and e.index == i then end end
  end
  return t
end
function compat.forget(obj) books[obj.id] = nil end

local menu_n = 0
function compat.add_context_menu_item(env, obj, label, fn, keep_open)
  local name = fn
  if type(fn) == "function" then
    menu_n = menu_n + 1
    name = "__menu_" .. menu_n
    env[name] = function(who) return fn(compat.color_of(who), compat.position(obj), obj) end
  end
  obj.menu_add(tostring(label), tostring(name))
end
function compat.clear_context_menu(obj) obj.menu_clear() end

function compat.add_hotkey(env, label, fn, on_up)
  if type(fn) ~= "function" then return nil end
  return game.hotkey(tostring(label), function(who, piece, pos, up) return fn(compat.color_of(who), piece, compat.Vector(pos.x or 0, 0, pos.z or 0), up) end, on_up and true or false)
end
function compat.add_global_context_menu_item(env, label, fn, keep_open)
  if type(fn) ~= "function" then return nil end
  return game.menu_add(tostring(label), function(who, pos) return fn(compat.color_of(who), compat.Vector(pos.x or 0, 0, pos.z or 0)) end)
end

local Pending = {}
Pending.__index = function(h, k)
  local piece = rawget(h, "__piece")
  if piece then return piece[k] end
  if k == "id" or k == "exists" then return nil end
  return function(...)
    local args = { ... }
    local q = rawget(h, "__queue")
    q[#q + 1] = { k, args }
  end
end

local function bind(h, def)
  if rawget(h, "__piece") then return rawget(h, "__piece") end
  local p = rawget(h, "__params")
  local pos, rot = p.position or {}, p.rotation or {}
  local piece = board.spawn{ def = def, x = vec(pos, "x", 1, 0), z = vec(pos, "z", 3, 0), rot = vec(rot, "y", 2, 0), label = p.nickname, guid = p.guid,
    state = p.state, tags = p.tags, memo = p.memo, script_state = p.script_state, note = p.description,
    script = type(p.script) == "string" and p.script ~= "" and p.script or nil }
  if p.gm_notes ~= nil then compat.set_gm_notes(piece, p.gm_notes) end
  rawset(h, "__piece", piece)
  for _, q in ipairs(rawget(h, "__queue")) do
    local f = piece[q[1]]
    if type(f) == "function" then f(table.unpack(q[2])) end
  end
  rawset(h, "__queue", {})
  if type(p.callback_function) == "function" then p.callback_function(piece) end
  return piece
end

function compat.spawn_object(p)
  p = p or {}
  local h = setmetatable({ __params = p, __queue = {} }, Pending)
  if p.def then bind(h, p.def) end
  return h
end

function compat.set_custom_object(obj, c)
  c = c or {}
  local def = c.def or c.image or c.diffuse
  if type(def) ~= "string" or def == "" or def:find("^https?://") then
    compat.todo(obj, "setCustomObject", def)
    return
  end
  if getmetatable(obj) == Pending then bind(obj, def) else obj.set_picture(def) end
end

function compat.piece_of(obj)
  if getmetatable(obj) == Pending then return rawget(obj, "__piece") end
  return obj
end

function compat.spawn_object_json(j)
  local ok, data = pcall(json.decode, j)
  if ok and type(data) == "table" then return compat.spawn_object_data(data) end
  compat.todo(nil, "spawnObjectJSON")
  return compat.spawn_object({})
end
function compat.spawn_object_data(d)
  d = d or {}
  local data = d.data or d
  local tr = data.Transform or {}
  return compat.spawn_object({
    position = d.position or { tr.posX, tr.posY, tr.posZ }, rotation = d.rotation or { tr.rotX, tr.rotY, tr.rotZ },
    nickname = data.Nickname, guid = data.GUID, def = data.def, callback_function = d.callback_function,
    state = data.State, tags = data.Tags, memo = data.Memo, script_state = data.LuaScriptState, script = data.LuaScript,
    description = data.Description, gm_notes = data.GMNotes,
  })
end

local series = {}
function compat.wait_time(fn, seconds, repeats)
  repeats = num(repeats, 1)
  seconds = num(seconds, 0)
  local id
  local function once()
    if repeats == -1 or repeats > 1 then
      if repeats > 1 then repeats = repeats - 1 end
      series[id] = wait.seconds(seconds, once)
    else
      series[id] = nil
    end
    fn()
  end
  id = wait.seconds(seconds, once)
  series[id] = id
  return id
end
function compat.wait_stop(id)
  if id == nil then return end
  wait.stop(series[id] or id)
  series[id] = nil
end
function compat.wait_condition(fn, cond, timeout, timeout_fn)
  return wait.condition(cond, fn, timeout, timeout_fn)
end
function compat.start_coroutine(obj, name)
  local target = compat.piece_of(obj)
  if target and target.call then target.call(name) else game.call(name) end
  return 1
end
function compat.string_color_to_rgb(name)
  local t = { White = { 1, 1, 1 }, Red = { 0.856, 0.1, 0.094 }, Blue = { 0.118, 0.53, 1 }, Green = { 0.192, 0.701, 0.168 }, Yellow = { 0.905, 0.898, 0.172 },
    Orange = { 0.956, 0.392, 0.113 }, Purple = { 0.627, 0.125, 0.941 }, Pink = { 0.96, 0.439, 0.807 }, Brown = { 0.443, 0.231, 0.09 }, Teal = { 0.129, 0.694, 0.607 },
    Grey = { 0.5, 0.5, 0.5 }, Black = { 0.25, 0.25, 0.25 } }
  local c = t[name] or { 1, 1, 1 }
  return { r = c[1], g = c[2], b = c[3], a = 1 }
end
function compat.cast(p)
  p = p or {}
  local o = p.origin or {}
  local ox, oz = vec(o, "x", 1, 0), vec(o, "z", 3, 0)
  local size = p.size or {}
  local hx, hz = vec(size, "x", 1, 1) / 2, vec(size, "z", 3, 1) / 2
  local r = num(p.size and p.size[1] or p.size and p.size.x, 1)
  local hits = {}
  for _, piece in ipairs(board.pieces()) do
    local q = piece.pos
    local inside
    if p.type == 1 then inside = math.abs(q.x - ox) < 0.001 and math.abs(q.z - oz) < 0.001
    elseif p.type == 2 then inside = (q.x - ox) ^ 2 + (q.z - oz) ^ 2 <= r * r
    else inside = math.abs(q.x - ox) <= hx and math.abs(q.z - oz) <= hz end
    if inside then hits[#hits + 1] = { hit_object = piece, point = compat.position(piece), normal = compat.Vector(0, 1, 0), distance = 0 } end
  end
  return hits
end
compat.Turns = setmetatable({}, { __index = function(_, k) if k == "turn_number" then return game.turn end if k == "enable" then return true end return nil end, __newindex = function() end })
compat.Notes = { getNotes = function() return compat.notes or "" end, setNotes = function(s) compat.notes = s end, addNotebookTab = function() end, getNotebookTabs = function() return {} end, editNotebookTab = function() end, removeNotebookTab = function() end }
compat.Lighting = setmetatable({ apply = function() end }, { __index = function() return 0 end, __newindex = function() end })
compat.Grid = setmetatable({}, { __index = function() return 0 end, __newindex = function() end })
compat.Hands = { enable = false, getHands = function() return {} end }
compat.MusicPlayer = { play = function() end, pause = function() end, skipBack = function() end, skipForward = function() end, setCurrentAudioclip = function() end }
compat.Backgrounds = { getBackground = function() return "" end, setBackground = function() end }
compat.Time = setmetatable({}, { __index = function(_, k) if k == "time" then return os.clock() end if k == "delta_time" then return 1 / 60 end if k == "fixed_delta_time" then return 1 / 60 end return 0 end })
local timers = {}
compat.Timer = {
  create = function(p)
    if not (p and p.function_name) then return nil end
    local reps = num(p.repetitions, 1)
    if reps == 0 then reps = -1 end
    local id = compat.wait_time(function() game.call(p.function_name, p.parameters) end, p.delay or 0, reps)
    if p.identifier ~= nil then
      if timers[p.identifier] then compat.wait_stop(timers[p.identifier]) end
      timers[p.identifier] = id
    end
    return id
  end,
  destroy = function(identifier)
    local id = timers[identifier]
    if id then compat.wait_stop(id); timers[identifier] = nil; return true end
    return false
  end,
}

local told = {}
function compat.todo(obj, name, ...)
  if not told[name] then
    told[name] = true
    print("TTS call not supported here: " .. tostring(name))
  end
  return nil
end

return compat
