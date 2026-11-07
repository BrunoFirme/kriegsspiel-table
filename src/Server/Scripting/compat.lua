local compat = {}

local function num(v, d) return tonumber(v) or d end
local function vec(v, k, i, d)
  if type(v) ~= "table" then return d end
  local x = v[k]
  if x == nil then x = v[i] end
  return num(x, d)
end
local TOKEN_AREA = 14
local function units(obj)
  local lu = obj.local_unit
  if type(lu) == "table" then return num(lu.x, 1), num(lu.z, 1) end
  local s = obj.size
  if not s then return 0.5, 0.5 end
  local k = obj.scale
  if type(k) ~= "number" or k <= 0 then k = 1 end
  local w, h = s.w / k, s.h / k
  if obj.shape ~= "alpha" then return h / 2, h / 2 end
  local px = obj.picture_size
  if not px or px.w <= 0 or px.h <= 0 then local u = math.sqrt(w * h / TOKEN_AREA); return u, u end
  local aspect = px.w / px.h
  return w / math.sqrt(TOKEN_AREA * aspect), h / math.sqrt(TOKEN_AREA / aspect)
end

compat.local_units = units

local PLAYER_COLORS = { "White", "Red", "Blue", "Green", "Yellow", "Orange", "Purple", "Pink", "Brown", "Teal", "Grey", "Black" }
local function umpires() return game.umpires() end

function compat.color(c)
  if type(c) == "string" then return c end
  if type(c) == "table" then return { vec(c, "r", 1, 0), vec(c, "g", 2, 0), vec(c, "b", 3, 0), vec(c, "a", 4, 1) } end
  return nil
end

local GM = "Black"
function compat.color_of(who)
  if type(who) ~= "table" then return "White" end
  if who.role == "host" then return GM end
  local i = 1
  for _, u in ipairs(umpires()) do
    if u.role ~= "host" then
      if u.id == who.id then return PLAYER_COLORS[math.min(i, #PLAYER_COLORS - 1)] end
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
    showInputDialog = function(desc, default, fn) return compat.text_dialog(who, "input", desc, default, fn) end,
    showMemoDialog = function(desc, default, fn) return compat.text_dialog(who, "memo", desc, default, fn) end,
    showOptionsDialog = function(desc, options, default, fn) return game.dialog(who, { kind = "options", title = tostring(desc or ""), text = tostring(default or ""), options = options or {} }, function(v, w) if v ~= nil and fn then local i = 0; for k, o in ipairs(options or {}) do if o == v then i = k end end; fn(v, i, compat.color_of(w)) end end) end,
    showColorDialog = function(default, fn) return game.dialog(who, { kind = "color", title = "Colour", text = tostring(default or "") }, function(v, w) if v ~= nil and fn then fn(compat.string_color_to_rgb(v), compat.color_of(w)) end end) end,
    clearSelectedObjects = function() end, kick = function() end, mute = function() end, changeColor = function() end,
  }
end

local PICTURE_WORDS = { "image", "picture", "url", "decal" }
function compat.text_dialog(who, kind, desc, default, fn)
  if type(default) == "function" then fn, default = default, nil end
  local title = tostring(desc or "")
  if kind == "input" then
    for _, word in ipairs(PICTURE_WORDS) do if title:lower():find(word, 1, true) then kind = "picture" break end end
  end
  return game.dialog(who, { kind = kind, title = title, text = tostring(default or "") }, function(v, w) if v ~= nil and fn then fn(v, compat.color_of(w)) end end)
end

compat.Player = setmetatable({
  getPlayers = function()
    local t = {}
    for _, u in ipairs(umpires()) do t[#t + 1] = compat.player(compat.color_of(u)) end
    return t
  end,
  getAvailableColors = function() return { "Red", "Blue", "Green", "Yellow", "Orange", "Purple", "Pink", "Brown", "Teal" } end,
  getColors = function() return PLAYER_COLORS end,
  getSpectators = function() return {} end,
  Action = {
    Copy = "copy", Cut = "cut", Delete = "delete", FlipIncremental = "flip_incremental", FlipOver = "flip", Group = "group", Paste = "paste",
    PickUp = "pick_up", Randomize = "randomize", RotateIncrementalLeft = "rotate_left", RotateIncrementalRight = "rotate_right", RotateOver = "rotate_over",
    Select = "select", Under = "under",
  },
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
  local ux, uz = units(obj)
  local w = obj.to_world(-vec(v, "x", 1, 0) * ux, vec(v, "z", 3, 0) * uz)
  return compat.Vector(w.x, vec(v, "y", 2, 0), w.z)
end
function compat.position_to_local(obj, v)
  local ux, uz = units(obj)
  local l = obj.to_local(vec(v, "x", 1, 0), vec(v, "z", 3, 0))
  return compat.Vector(-l.x / ux, vec(v, "y", 2, 0), l.z / uz)
end
function compat.transform_forward(obj) local a = math.rad(obj.rot or 0); return compat.Vector(math.sin(a), 0, math.cos(a)) end
function compat.transform_right(obj) local a = math.rad(obj.rot or 0); return compat.Vector(math.cos(a), 0, -math.sin(a)) end
function compat.transform_up(obj) return compat.Vector(0, 1, 0) end

local function hex2(s, i) return tonumber(s:sub(i, i + 1), 16) / 255 end
function compat.color_tint(obj)
  local t = obj.tint
  if type(t) == "string" and #t >= 7 and t:sub(1, 1) == "#" then return compat.Color(hex2(t, 2), hex2(t, 4), hex2(t, 6), #t >= 9 and hex2(t, 8) or 1) end
  return compat.Color(1, 1, 1, 1)
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
compat.is_piece = is_piece
function compat.set_prop(obj, k, v, as_written)
  if not is_piece(obj) then if type(obj) == "table" then obj[as_written or k] = v end return end
  if k == "memo" then obj.set_memo(v) return end
  if k == "script_state" then obj.set_script_state(v) return end
  if k == "locked" then obj.set_locked(v and true or false) return end
  if k == "sticky" then obj.set_sticky(v ~= false) return end
  if k == "interactable" then obj.set_interactable(v ~= false) return end
  if k == "drag_selectable" then obj.set_box_selected(v ~= false) return end
  if k == "measure_movement" then obj.set_measures_moves(v == true) return end
  if k == "value" then obj.set_worth(tonumber(v) or 0) return end
  if k == "held_spin_index" then obj.set_rot((tonumber(v) or 0) * 15) return end
  if k == "held_flip_index" then if ((tonumber(v) or 0) % 24 >= 6 and (tonumber(v) or 0) % 24 < 18) ~= (obj.face_down == true) then obj.flip() end return end
  if k == "value_flags" then obj.set_worth(obj.worth, tonumber(v) or 0) return end
  pbag(obj)[k] = v
end
function compat.get_prop(obj, k, d, as_written)
  if not is_piece(obj) then
    if type(obj) ~= "table" then return d end
    if as_written then return obj[as_written] end
    if obj[k] ~= nil then return obj[k] end
    return d
  end
  if k == "memo" then local m = obj.memo; if m == nil or m == "" then return d end return m end
  if k == "script_state" then local v = obj.script_state; if v == nil or v == "" then return d end return v end
  if k == "locked" then return obj.locked == true end
  if k == "sticky" then return obj.sticky ~= false end
  if k == "interactable" then return obj.interactable ~= false end
  if k == "drag_selectable" then return obj.box_selected ~= false end
  if k == "measure_movement" then return obj.measures_moves == true end
  if k == "value" then return obj.worth end
  if k == "held_spin_index" then return math.floor(((obj.rot or 0) % 360) / 15 + 0.5) % 24 end
  if k == "held_flip_index" then return obj.face_down and 12 or 0 end
  if k == "pick_up_position" then local at = obj.picked_up or { x = obj.pos.x, y = obj.y or 0, z = obj.pos.z }; return compat.Vector(at.x, at.y, at.z) end
  if k == "pick_up_rotation" then local at = obj.picked_up; return compat.Vector(0, at and at.rot or obj.rot or 0, obj.face_down and 180 or 0) end
  if k == "value_flags" then return obj.worth_groups end
  local v = pbag(obj)[k]; if v == nil then return d end return v
end
function compat.log(value, label, tags)
  local text = type(value) == "table" and json.encode(value) or tostring(value)
  if label ~= nil and label ~= "" then text = tostring(label) .. ": " .. text end
  print(text)
end
function compat.set_lock(obj, v) obj.set_locked(v and true or false) end
function compat.get_lock(obj) return obj.locked == true end
function compat.set_memo(obj, s) obj.set_memo(s) end
function compat.get_memo(obj) return obj.memo end
function compat.set_gm_notes(obj, s) pbag(obj).gm_notes = s end
function compat.get_gm_notes(obj) return pbag(obj).gm_notes or obj.gm_notes or "" end
function compat.set_interactable(obj, v) pbag(obj).interactable = v end
function compat.highlight(obj, color, seconds) end
function compat.highlight_off(obj) end
function compat.set_vector_lines(obj, lines)
  pbag(obj).lines = lines
  local ux, uz = units(obj)
  local ours = {}
  for _, l in ipairs(lines or {}) do
    local pts = {}
    for _, p in ipairs(l.points or {}) do pts[#pts + 1] = { x = -vec(p, "x", 1, 0) * ux, z = vec(p, "z", 3, 0) * uz } end
    if #pts >= 2 then ours[#ours + 1] = { points = pts, color = compat.color(l.color), width = num(l.thickness, 0.1), loop = l.loop == true } end
  end
  obj.set_lines(ours)
end
function compat.get_vector_lines(obj) return pbag(obj).lines or {} end
local global_lines = { given = {}, ids = {} }
function compat.global_vector_lines() return global_lines.given end
function compat.set_global_vector_lines(lines)
  for _, id in ipairs(global_lines.ids) do board.erase(id) end
  global_lines = { given = lines or {}, ids = {} }
  for _, l in ipairs(lines or {}) do
    local pts = {}
    for _, p in ipairs(l.points or {}) do pts[#pts + 1] = { x = vec(p, "x", 1, 0), z = vec(p, "z", 3, 0) } end
    if l.loop and #pts >= 2 then pts[#pts + 1] = pts[1] end
    if #pts >= 2 then
      local id = board.draw({ points = pts, color = compat.color(l.color), width = num(l.thickness, 0.1) })
      if id then global_lines.ids[#global_lines.ids + 1] = id end
    end
  end
end
local web_pictures = {}
local function is_web(url) return type(url) == "string" and url:find("://", 1, true) ~= nil end
local function our_decal(obj, d)
  local url = d and d.url
  if type(url) ~= "string" or url == "" then return nil end
  if is_web(url) then url = web_pictures[url] if not url then return nil end end
  local ux, uz = units(obj)
  local px, py, pz = vec(d.position, "x", 1, 0), vec(d.position, "y", 2, 0), vec(d.position, "z", 3, 0)
  local back = py < 0
  local rot = vec(d.rotation, "y", 2, 0) - 180
  local c, s = math.cos(math.rad(rot)), math.sin(math.rad(rot))
  return {
    picture = url, name = tostring(d.name or ""), face = back and "back" or "front",
    x = (back and px or -px) * ux, z = pz * uz,
    w = math.abs(vec(d.scale, "x", 1, 1)) * math.sqrt((ux * c) ^ 2 + (uz * s) ^ 2),
    h = math.abs(vec(d.scale, "y", 2, 1)) * math.sqrt((ux * s) ^ 2 + (uz * c) ^ 2),
    rot = rot,
  }
end
local function apply_decals(obj)
  local ours = {}
  for _, dc in ipairs(pbag(obj).decals or {}) do ours[#ours + 1] = our_decal(obj, dc) end
  local ok, err = pcall(obj.set_decals, ours)
  if not ok then print("setDecals: " .. tostring(err)) end
  return ok
end
function compat.set_decals(obj, d)
  local bag = pbag(obj)
  bag.decals = d or {}
  bag.decal_round = (bag.decal_round or 0) + 1
  local round, waiting = bag.decal_round, 0
  for _, dc in ipairs(bag.decals) do
    local url = dc and dc.url
    if is_web(url) and not web_pictures[url] then
      waiting = waiting + 1
      game.fetch_picture(url, function(name)
        web_pictures[url] = name or false
        waiting = waiting - 1
        if waiting == 0 and bag.decal_round == round then apply_decals(obj) end
      end)
    end
  end
  return apply_decals(obj)
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
function compat.get_objects(obj)
  local t = {}
  for i, s in ipairs(obj.stowed or {}) do
    t[#t + 1] = { guid = s.guid, name = s.name, nickname = s.name, index = i - 1, description = "", gm_notes = "", memo = "", lua_script = "", lua_script_state = "", tags = {} }
  end
  return t
end
function compat.get_quantity(obj) return #(obj.stowed or {}) end
function compat.flip(obj)
  local faces = obj.faces
  if faces == nil or #faces < 2 then return obj.flip() end
  local now = faces[obj.face].up
  local best, most = obj.face, -2
  for i, f in ipairs(faces) do
    local d = -(f.up.x * now.x + f.up.y * now.y + f.up.z * now.z)
    if d > most then most = d best = i end
  end
  return obj.set_face(best)
end
local function page_offset(obj)
  local o = pbag(obj).page_offset
  if o == nil then o = obj.page_offset or 0 end
  return o
end
function compat.book_offset(obj) return page_offset(obj) end
function compat.set_book_offset(obj, n) pbag(obj).page_offset = tonumber(n) or 0 end
function compat.book_page(obj, use_offset)
  local p = (obj.state or 1) - 1
  if use_offset then return p + page_offset(obj) + 1 end
  return p
end
function compat.set_book_page(obj, page, use_offset)
  page = tonumber(page) or 0
  if use_offset then page = page - (page_offset(obj) + 1) end
  local n = math.max(#(obj.states or {}), 1)
  if page < 0 or page >= n or page == (obj.state or 1) - 1 then return false end
  obj.set_state(page + 1)
  return true
end
function compat.book_highlight(obj, x1, y1, x2, y2)
  local p = compat.piece_of(obj)
  if type(p) ~= "table" or p.set_book_highlight == nil then return false end
  local book = p.source ~= nil and p.source.type == "Custom_PDF"
  for _, t in ipairs(p.tags or {}) do if t == "book" then book = true end end
  if not book then return false end
  if x1 == nil then p.set_book_highlight(nil) return true end
  p.set_book_highlight({ left = tonumber(x1) or 0, bottom = tonumber(y1) or 0, right = tonumber(x2) or 0, top = tonumber(y2) or 0 })
  return true
end
compat.AssetBundle = {}
function compat.bundle(obj)
  return {
    getLoopingEffects = function() return { { index = 0, name = obj.name or obj.kind_name } } end,
    getTriggerEffects = function() return { { index = 0, name = obj.name or obj.kind_name } } end,
    getLoopingEffectIndex = function() if obj.effect_on then return 0 end return -1 end,
    playLoopingEffect = function(i) obj.set_effect_on((tonumber(i) or 0) >= 0) return true end,
    playTriggerEffect = function(i) obj.play_effect(tonumber(i) or 0) return true end,
  }
end
function compat.randomize(obj) return obj.roll() end
function compat.roll(obj) return obj.roll() end
function compat.shuffle_states(obj)
  local n = #(obj.states or {})
  if n < 2 then return nil end
  local pick = math.random(n - 1)
  if pick >= obj.state then pick = pick + 1 end
  obj.set_state(pick)
  return obj
end
function compat.get_value(obj)
  if obj.face ~= nil then return obj.face end
  if obj.lettering ~= nil then return obj.lettering.text end
  return nil
end
function compat.set_value(obj, v)
  if obj.face ~= nil then return obj.set_face(tonumber(v) or 0) end
  if obj.lettering ~= nil then obj.set_lettering({ text = tostring(v) }) return true end
  return false
end
function compat.text_tool(obj)
  local function now() return obj.lettering or { text = "", size = 64, color = "#FFFFFFFF" } end
  return {
    getValue = function() return now().text end,
    setValue = function(s) obj.set_lettering({ text = tostring(s) }) return true end,
    getFontSize = function() return now().size end,
    setFontSize = function(n) obj.set_lettering({ size = tonumber(n) }) return true end,
    getFontColor = function() return compat.Color.fromHex(now().color) end,
    setFontColor = function(c) obj.set_lettering({ color = compat.hex_of(compat.color_arg(c)) }) return true end,
  }
end
function compat.get_rotation_value(obj) return obj.value end
function compat.set_rotation_value(obj, v) return obj.set_value(v) end
function compat.get_rotation_values(obj)
  local t = {}
  for _, f in ipairs(obj.faces or {}) do
    t[#t + 1] = { value = f.value, rotation = compat.Vector(f.rotation.x, f.rotation.y, f.rotation.z) }
  end
  return t
end
function compat.set_rotation_values(obj, list)
  if type(list) ~= "table" then return false end
  local faces = {}
  for _, f in ipairs(list) do
    if type(f) ~= "table" then return false end
    local r = f.rotation or {}
    faces[#faces + 1] = { value = f.value, rotation = { x = vec(r, "x", 1, 0), y = vec(r, "y", 2, 0), z = vec(r, "z", 3, 0) } }
  end
  return obj.set_faces(faces)
end
function compat.take_object(obj, params)
  params = type(params) == "table" and params or {}
  local which = params.guid or (type(params.index) == "number" and params.index + 1) or nil
  local place = nil
  if type(params.position) == "table" then place = { x = vec(params.position, "x", 1, 0), z = vec(params.position, "z", 3, 0) } end
  local rot = type(params.rotation) == "table" and vec(params.rotation, "y", 2, nil) or nil
  local p = obj.unstow(which, place, rot)
  if p == nil then return nil end
  local cb = params.callback_function
  if type(cb) == "string" then cb = _G[cb] end
  if type(cb) == "function" then cb(p) end
  return p
end
function compat.put_object(obj, piece)
  if is_piece(piece) then obj.stow(piece) end
  return obj
end
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

local function module_named(env, name)
  if type(env) ~= "table" or (type(env.__tts_bound) == "table" and env.__tts_bound[name]) then return name end
  local tramp = "__tts_" .. name
  if rawget(env, tramp) == nil then
    env[tramp] = function(...)
      local f = env[name]
      if type(f) == "function" then return compat.module_handler(f)(...) end
    end
  end
  return tramp
end

local function handler(name, owner, obj, env)
  if name == nil or name == "" then return nil end
  name = tostring(name)
  if owner == compat.Global then return "global:" .. name end
  if is_piece(owner) and type(obj) == "table" then
    local oid = owner.id
    if type(oid) == "string" and oid ~= obj.id then return oid .. "/" .. name end
  end
  return module_named(env, name)
end

function compat.module_handler(fn)
  return function(a, b, c, d, ...)
    if type(a) == "table" and type(a.id) == "number" and is_piece(d) then
      if type(b) == "string" then return fn(d, compat.color_of(a), b, false) end
      return fn(d, compat.color_of(a), b)
    end
    return fn(a, b, c, d, ...)
  end
end

local BUTTON_EM, TOKEN_BUTTON_EM, INPUT_SCALE = 0.87, 1.87, 2.2
local VALIDATION = { [2] = "integer", [3] = "float", [4] = "alphanumeric", [5] = "username", [6] = "name" }
local TABS = { [2] = "next", [3] = "spaces" }
local ALIGN = { [2] = "left", [3] = "center", [4] = "right" }
compat.module_defaults = {
  width = 100, height = 100, font_size = 100, alignment = 1,
  color = { 1, 1, 1, 1 }, font_color = { 0, 0, 0, 1 },
  position = { 0, 0, 0 }, rotation = { 0, 0, 0 }, scale = { 1, 1, 1 },
  value = "", validation = 1, tab = 2,
}
local D = compat.module_defaults

function compat.widget_spec(obj, t, kind, env)
  local ux, uz = units(obj)
  local px, pz = vec(t.position, "x", 1, 0), vec(t.position, "z", 3, 0)
  local rx, ry, rz = vec(t.rotation, "x", 1, 0), vec(t.rotation, "y", 2, 0), vec(t.rotation, "z", 3, 0)
  local k = kind == "input" and INPUT_SCALE or 1
  local em = kind ~= "input" and obj.shape == "alpha" and TOKEN_BUTTON_EM or BUTTON_EM
  local sx, sz = math.abs(vec(t.scale, "x", 1, 1)) * k, math.abs(vec(t.scale, "z", 3, 1)) * k
  local turned = math.abs(((rz % 360) + 360) % 360 - 180) < 90 or math.abs(((rx % 360) + 360) % 360 - 180) < 90
  local y = vec(t.position, "y", 2, 0)
  local on_top = y > 0
  local back = turned and not on_top
  local s = {
    face = back and "back" or "front", kind = turned and on_top and "label" or kind, inside = not turned and y < (obj.source and obj.source.top or 0),
    x = (back and -px or px) * ux, z = pz * uz, rot = ry,
    w = num(t.width, D.width) / 1000 * sx * ux, h = num(t.height, D.height) / 1000 * sz * uz,
    font = num(t.font_size, D.font_size) / 1000 * sx * uz * em,
    color = compat.color(t.color) or D.color, text_color = compat.color(t.font_color) or D.font_color, tooltip = t.tooltip,
    align = ALIGN[num(t.alignment, D.alignment)] or "center",
    hover_color = compat.color(t.hover_color), press_color = compat.color(t.press_color),
  }
  if kind == "input" then
    s.value = tostring(t.value or D.value); s.placeholder = tostring(t.label or "")
    s.validation = VALIDATION[num(t.validation, D.validation)]; s.tab = TABS[num(t.tab, D.tab)] or "none"
    s.on_change = handler(t.input_function, t.function_owner, obj, env)
  else
    s.label = tostring(t.label or "")
    s.on_click = handler(t.click_function, t.function_owner, obj, env)
  end
  return s
end

function compat.widget_patch(obj, t, kind, full, env)
  local p = {}
  local geo = t.position or t.rotation or t.scale or t.width or t.height or t.font_size
  if geo then
    local s = compat.widget_spec(obj, full or t, kind, env)
    if t.position or t.rotation then p.x = s.x; p.z = s.z; p.face = s.face; p.kind = s.kind; p.rot = s.rot; p.inside = s.inside end
    if t.width or t.scale then p.w = s.w end
    if t.height or t.scale then p.h = s.h end
    if t.font_size or t.scale then p.font = s.font end
  end
  if t.label ~= nil then if kind == "input" then p.placeholder = tostring(t.label) else p.label = tostring(t.label) end end
  if t.value ~= nil then p.value = tostring(t.value) end
  if t.color ~= nil then p.color = compat.color(t.color) end
  if t.font_color ~= nil then p.text_color = compat.color(t.font_color) end
  if t.tooltip ~= nil then p.tooltip = t.tooltip end
  if t.hover_color ~= nil then p.hover_color = compat.color(t.hover_color) end
  if t.press_color ~= nil then p.press_color = compat.color(t.press_color) end
  if t.validation ~= nil and kind == "input" then p.validation = VALIDATION[num(t.validation, 1)] or "any" end
  if t.tab ~= nil and kind == "input" then p.tab = TABS[num(t.tab, 2)] or "none" end
  if t.alignment ~= nil then p.align = ALIGN[num(t.alignment, 3)] or "center" end
  if t.click_function ~= nil then p.on_click = handler(t.click_function, t.function_owner, obj, env) end
  if t.input_function ~= nil then p.on_change = handler(t.input_function, t.function_owner, obj, env) end
  return p
end

local function drawn(obj, e, kind)
  if e.spec.inside or e.gone then return nil end
  if kind == "input" then return obj.input(e.spec) end
  return obj.button(e.spec)
end

local function rebuild(obj, b)
  obj.clear_widgets()
  for _, e in ipairs(b.buttons) do e.index = drawn(obj, e, "button") end
  for _, e in ipairs(b.inputs) do e.index = drawn(obj, e, "input") end
end

local function refused(what, fn)
  print(what .. ": " .. fn .. " was null or an empty string")
  return false
end
function compat.create_button(obj, t, env)
  if type(t) ~= "table" then return false end
  if t.click_function == nil or t.click_function == "" then return refused("createButton", "click_function") end
  local b = book(obj)
  local e = { spec = compat.widget_spec(obj, t, "button", env), given = t }
  e.index = drawn(obj, e, "button")
  b.buttons[#b.buttons + 1] = e
  return true
end
function compat.create_input(obj, t, env)
  if type(t) ~= "table" then return false end
  if t.input_function == nil or t.input_function == "" then return refused("createInput", "input_function") end
  local b = book(obj)
  local e = { spec = compat.widget_spec(obj, t, "input", env), given = t }
  e.index = drawn(obj, e, "input")
  b.inputs[#b.inputs + 1] = e
  return true
end
local function edit(obj, list, t, kind, env)
  local e = list[num(t.index, 0) + 1]
  if not e then return false end
  local full = {}
  for k, v in pairs(e.given) do full[k] = v end
  for k, v in pairs(t) do full[k] = v end
  local fn = kind == "input" and "input_function" or "click_function"
  if full[fn] == nil or full[fn] == "" then return refused(kind == "input" and "editInput" or "editButton", fn) end
  local patch = compat.widget_patch(obj, t, kind, full, env)
  local was = e.spec.inside
  for k, v in pairs(patch) do e.spec[k] = v end
  e.given = full
  patch.inside = nil
  if e.spec.inside ~= was then rebuild(obj, book(obj)) elseif e.index then obj.edit(e.index, patch) end
  return true
end
function compat.edit_button(obj, t, env) return edit(obj, book(obj).buttons, t, "button", env) end
function compat.edit_input(obj, t, env) return edit(obj, book(obj).inputs, t, "input", env) end
local function remove(obj, which, i, what)
  local b = book(obj)
  local e = b[which][num(i, 0) + 1]
  if not e then
    print(what .. ": Could not find matching " .. (which == "inputs" and "input" or "button") .. " index")
    return false
  end
  if e.gone then return true end
  e.gone = true
  rebuild(obj, b)
  wait.frames(1, function()
    for k, x in ipairs(b[which]) do if x == e then table.remove(b[which], k); return end end
  end)
  return true
end
function compat.remove_button(obj, i) return remove(obj, "buttons", i, "removeButton") end
function compat.remove_input(obj, i) return remove(obj, "inputs", i, "removeInput") end
function compat.clear_buttons(obj) local b = book(obj); b.buttons = {}; rebuild(obj, b) end
function compat.clear_inputs(obj) local b = book(obj); b.inputs = {}; rebuild(obj, b) end
local function widget_list(list)
  local t = {}
  for i, e in ipairs(list) do local c = {}; for k, v in pairs(e.given) do c[k] = v end; c.index = i - 1; t[#t + 1] = c end
  return t
end
function compat.get_buttons(obj)
  local b = book(obj)
  if #b.buttons == 0 then return nil end
  return widget_list(b.buttons)
end
function compat.get_inputs(obj)
  if #book(obj).inputs == 0 then return nil end
  local t = widget_list(book(obj).inputs)
  local live = obj.widgets()
  for i, e in ipairs(book(obj).inputs) do
    local w = e.index and live[e.index]
    if w and w.value ~= nil then t[i].value = w.value; e.given.value = w.value end
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
    state = p.state, tags = p.tags or {}, memo = p.memo, script_state = p.script_state, note = p.description,
    script = type(p.script) == "string" and p.script ~= "" and p.script or nil }
  if p.gm_notes ~= nil then compat.set_gm_notes(piece, p.gm_notes) end
  rawset(h, "__piece", piece)
  for _, q in ipairs(rawget(h, "__queue")) do
    local f = piece[q[1]]
    if type(f) == "function" then f(table.unpack(q[2])) end
  end
  rawset(h, "__queue", {})
  compat.spawned(piece, p)
  return piece
end

function compat.spawn_object(p)
  p = p or {}
  local h = setmetatable({ __params = p, __queue = {} }, Pending)
  if p.def then bind(h, p.def) end
  return h
end

function compat.kind_for_link(link, other, other_key)
  if type(link) ~= "string" or link == "" then return nil end
  local found = board.kinds{ link = link }
  for _, k in ipairs(found) do
    local links = k.source and k.source.links or {}
    if other == nil or other == "" or links[other_key] == other then return k.id end
  end
  return found[1] and found[1].id or nil
end
function compat.set_custom_object(obj, c)
  c = c or {}
  local def = c.def or c.image or c.diffuse
  local known = c.def == nil and (c.mesh and compat.kind_for_link(c.mesh, c.diffuse, "diffuse") or c.assetbundle and compat.kind_for_link(c.assetbundle)
    or compat.kind_for_link(c.image, c.image_secondary, "image_secondary"))
  if known then def = known end
  if type(def) == "string" and def:find("^https?://") and c.mesh == nil then
    local p = compat.piece_of(obj)
    if p ~= nil then p.set_face_picture(def) return end
  end
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
    nickname = data.Nickname, guid = data.GUID, def = data.def, callback_function = d.callback_function, callback = d.callback, callback_owner = d.callback_owner, params = d.params,
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
function compat.string_color_to_rgb(name) return compat.Color.fromString(name) end
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
local NOTES_TAG, TAB_TAG, TITLE_LINE, BODY_LINE, NOTE_DEF, NOTE_GAP = "notes", "notebook", "Name", "Note", "std.effects.note", 1
local function note_cards(tag)
  local list = board.pieces({ tag = tag }) or {}
  table.sort(list, function(a, b) return a.id < b.id end)
  return list
end
local function note_line(card, name) local t = card.text or {} return t[name] or "" end
local function new_note_card(title, body, tag)
  local all = note_cards(TAB_TAG)
  for _, c in ipairs(note_cards(NOTES_TAG)) do all[#all + 1] = c end
  local like = all[#all]
  local at = like and { x = like.pos.x, z = like.pos.z - like.size.h - NOTE_GAP } or { x = 0, z = 0 }
  local ok, card = pcall(board.spawn, { def = like and like.def or NOTE_DEF, x = at.x, z = at.z, rot = like and like.rot or 0, tags = { tag } })
  if not ok or card == nil then compat.todo(nil, "Notes", "no note card to write on") return nil end
  card.set_text(TITLE_LINE, tostring(title or ""))
  card.set_text(BODY_LINE, tostring(body or ""))
  return card
end
compat.Notes = {
  getNotes = function() local c = note_cards(NOTES_TAG)[1] return c and note_line(c, BODY_LINE) or "" end,
  setNotes = function(s)
    local c = note_cards(NOTES_TAG)[1]
    if c == nil then return new_note_card("Notes", s, NOTES_TAG) ~= nil end
    c.set_text(BODY_LINE, tostring(s or ""))
    return true
  end,
  getNotebookTabs = function()
    local t = {}
    for i, c in ipairs(note_cards(TAB_TAG)) do t[#t + 1] = { index = i - 1, title = note_line(c, TITLE_LINE), body = note_line(c, BODY_LINE), color = "Grey" } end
    return t
  end,
  addNotebookTab = function(p)
    p = type(p) == "table" and p or {}
    if new_note_card(p.title, p.body, TAB_TAG) == nil then return -1 end
    return #note_cards(TAB_TAG) - 1
  end,
  editNotebookTab = function(p)
    p = type(p) == "table" and p or {}
    local c = note_cards(TAB_TAG)[(tonumber(p.index) or -1) + 1]
    if c == nil then return false end
    if p.title ~= nil then c.set_text(TITLE_LINE, tostring(p.title)) end
    if p.body ~= nil then c.set_text(BODY_LINE, tostring(p.body)) end
    return true
  end,
  removeNotebookTab = function(i)
    local c = note_cards(TAB_TAG)[(tonumber(i) or -1) + 1]
    if c == nil then return false end
    c.delete()
    return true
  end,
}
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
function compat.copy_table(t, seen)
  if type(t) ~= "table" then return t end
  seen = seen or {}
  if seen[t] then return seen[t] end
  local out = {}
  seen[t] = out
  for k, v in pairs(t) do out[compat.copy_table(k, seen)] = compat.copy_table(v, seen) end
  return setmetatable(out, getmetatable(t))
end
function compat.wait_stop_all()
  local ids = {}
  for id in pairs(series) do ids[#ids + 1] = id end
  for _, id in ipairs(ids) do compat.wait_stop(id) end
end
function compat.has_any_tag(obj, tags)
  for _, t in ipairs(tags or {}) do if compat.has_tag(obj, t) then return true end end
  return false
end
function compat.has_matching_tag(obj, other)
  if type(other) ~= "table" then return false end
  for _, t in ipairs(obj.tags or {}) do if compat.has_tag(other, t) then return true end end
  return false
end
function compat.all_tags()
  local seen, out = {}, {}
  for _, p in ipairs(board.pieces()) do
    for _, t in ipairs(p.tags or {}) do if not seen[t] then seen[t] = true; out[#out + 1] = t end end
  end
  table.sort(out)
  return out
end
function compat.erase_all()
  for _, s in ipairs(board.strokes()) do board.erase(s.id) end
end
function compat.todo(obj, name, ...)
  if not told[name] then
    told[name] = true
    print("Module call not supported here: " .. tostring(name))
  end
  return nil
end

return compat
