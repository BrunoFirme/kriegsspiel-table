-- A unit block, written for these tests the way Tabletop Simulator module pieces are written: its name on the
-- face, three counters on the back with a + and a - for each, a formation mark on the face and the four buttons
-- that choose it, and a box on the back to type the name in. It keeps the habits such pieces have, since those
-- are what an import has to cope with: the whole state saved as one JSON table, buttons edited by their index,
-- and the name set with `name = value, self.editButton{...}`, which edits the face with the name from before.

---adjust these values to change what the counters track and how far they go---
AMMO_MIN, AMMO_MAX = 0, 9
MORALE_MIN, MORALE_MAX = 1, 9
STRENGTH_MIN, STRENGTH_MAX = 0, 9
SMALL, MEDIUM = 3, 6        -- the block grows a size as Strength passes these, and shrinks back
TEXT = 'White'
FORM_ON, FORM_OFF = {1, 0, 0, 1}, {0, 0, 0, 1}

name = "Unit"
ammo, morale, strength = 0, 1, 1
form = 1

function none() end

function onSave()
  local state = { name = name, ammo = ammo, morale = morale, strength = strength, form = form }
  return JSON.encode(state)
end

function onLoad(saved)
  if saved ~= nil and saved ~= "" then
    local state = JSON.decode(saved)
    name, ammo, morale, strength, form = state.name, state.ammo, state.morale, state.strength, state.form
  end
  if form == nil then form = 1 end
  makeButtons()
end

-- Button indexes, in the order they are made: 0 the name, 1-3 the counters, 4 the formation mark,
-- 5-8 the formation buttons, 9-14 the + and - buttons. The name box is an input of its own.
function makeButtons()
  self.createButton({ click_function = "none", function_owner = self, label = name,
    position = {0, 0.26, -0.7}, width = 0, height = 0, font_size = 225, font_color = TEXT })

  self.createButton({ click_function = "fireSmoke", function_owner = self, label = ammo, tooltip = "Ammo",
    position = {1, 0, 0}, rotation = {0, 0, 180}, scale = {2.5, 2.5, 2.5},
    width = 160, height = 160, font_size = 160, font_color = TEXT, color = {0, 0, 0} })
  self.createButton({ click_function = "none", function_owner = self, label = morale, tooltip = "Morale",
    position = {0, 0, 0}, rotation = {0, 0, 180}, scale = {2.5, 2.5, 2.5},
    width = 160, height = 160, font_size = 160, font_color = TEXT, color = {0, 0, 0} })
  self.createButton({ click_function = "sendSkirmishers", function_owner = self, label = strength, tooltip = "Strength",
    position = {-1, 0, 0}, rotation = {0, 0, 180}, scale = {2.5, 2.5, 2.5},
    width = 160, height = 160, font_size = 160, font_color = TEXT, color = {0, 0, 0} })

  self.createButton(markFor(form))

  -- What the small buttons share is filled in here; each names its own function where it is made.
  local function chooser(b, x, on)
    b.function_owner, b.position, b.rotation, b.scale = self, {x, 0, -0.7}, {0, 0, 180}, {1.8, 1.8, 1.8}
    b.width, b.height, b.font_size, b.font_color, b.color = 100, 80, 70, {1, 1, 1, 1}, on and FORM_ON or FORM_OFF
    return b
  end
  self.createButton(chooser({ click_function = "formLine", label = "L", tooltip = "Line" }, 1.45, form == 1))
  self.createButton(chooser({ click_function = "formSquare", label = "S", tooltip = "Square" }, 1.1, form == 2))
  self.createButton(chooser({ click_function = "formColumn", label = "C", tooltip = "Column of march" }, -1.1, form == 3))
  self.createButton(chooser({ click_function = "formAssault", label = "A", tooltip = "Assault column" }, -1.45, form == 4))

  local function step(b, x)
    b.function_owner, b.tooltip, b.position, b.rotation, b.scale = self, b.label, {x, 0, 0.6}, {0, 0, 180}, {3, 3, 3}
    b.width, b.height, b.font_size, b.font_color, b.color = 80, 80, 70, {1, 1, 1, 1}, {0, 0, 0, 1}
    return b
  end
  self.createButton(step({ click_function = "ammoUp", label = "+" }, 0.8))
  self.createButton(step({ click_function = "ammoDown", label = "-" }, 1.2))
  self.createButton(step({ click_function = "moraleUp", label = "+" }, -0.2))
  self.createButton(step({ click_function = "moraleDown", label = "-" }, 0.2))
  self.createButton(step({ click_function = "strengthUp", label = "+" }, -1.2))
  self.createButton(step({ click_function = "strengthDown", label = "-" }, -0.8))

  self.createInput({ input_function = "rename", function_owner = self, value = name, label = "Unit",
    alignment = 3, position = {0, 0, -0.7}, rotation = {0, 0, 180}, scale = {x = 1.5, y = 1.5, z = 1.5},
    width = 600, height = 110, font_size = 85, font_color = {1, 1, 1, 1}, color = {0.1, 0.1, 0.1, 1} })
end

-- The mark on the face for each formation: nothing for a line, a square, arrows for a column, A for an assault.
function markFor(f)
  local mark = { index = 4, click_function = "none", function_owner = self, label = "", rotation = {0, 0, 0},
    position = {1, 0.25, 0.25}, scale = {2, 2, 2}, width = 0, height = 0, font_size = 160, font_color = {1, 1, 1, 1} }
  if f == 2 then
    mark.label, mark.position, mark.scale = "■", {0, 0.26, 0.25}, {3, 3, 3}
    mark.width, mark.height, mark.font_size, mark.color = 275, 275, 300, {0, 0, 0, 1}
  elseif f == 3 then
    mark.label, mark.position, mark.scale = "◄ ◄◄◄", {1.2, 0.23, 0.25}, {4, 4, 4}
  elseif f == 4 then
    mark.label, mark.position, mark.rotation, mark.font_size, mark.font_color = "↑A↑", {2, 0.25, 0.25}, {0, 90, 0}, 200, {1, 0, 0, 1}
  end
  return mark
end

function setForm(f, says)
  form = f
  self.editButton(markFor(f))
  for i = 1, 4 do
    self.editButton({ index = 4 + i, color = (i == f) and FORM_ON or FORM_OFF })
  end
  print(says)
end

function formLine() setForm(1, "Line formed") end
function formSquare() setForm(2, "Square formed") end
function formColumn() setForm(3, "Column of march formed") end
function formAssault() setForm(4, "Assault column formed") end

function rename(obj, color, value)
  name = value,
  self.editButton({index = 0, label = name})
end

function ammoUp()
  ammo = math.min(ammo + 1, AMMO_MAX)
  self.editButton({index = 1, label = ammo})
end

function ammoDown()
  ammo = ammo - 1
  if ammo < AMMO_MIN then ammo = AMMO_MIN print("Out of ammunition") end
  self.editButton({index = 1, label = ammo})
end

function moraleUp()
  morale = math.min(morale + 1, MORALE_MAX)
  self.editButton({index = 2, label = morale})
end

function moraleDown()
  morale = math.max(morale - 1, MORALE_MIN)
  self.editButton({index = 2, label = morale})
end

function strengthUp()
  if strength >= STRENGTH_MAX then strength = STRENGTH_MAX print("Strength is at its most") return end
  if strength == SMALL or strength == MEDIUM then self.scale(1.25) end
  strength = strength + 1
  self.editButton({index = 3, label = strength})
end

function strengthDown()
  if strength <= STRENGTH_MIN then strength = STRENGTH_MIN return end
  strength = strength - 1
  if strength == SMALL or strength == MEDIUM then self.scale(0.8) end
  self.editButton({index = 3, label = strength})
end

-- Where a token this block puts down lands: just in front of it.
function inFront()
  return self.positionToWorld({0, -0.25, -1.75})
end

-- A volley: a smoke token in front of the block, and one round of ammunition less.
function fireSmoke()
  local token = spawnObject({ type = 'Custom_Token', position = inFront(), rotation = self.getRotation(),
    scale = {x = 0.28, y = 1, z = 0.28}, snap_to_grid = false })
  token.setCustomObject({
    image = "https://steamusercontent-a.akamaihd.net/ugc/1000000000000000001/0123456789ABCDEF0123456789ABCDEF01234567/",
    thickness = 0.2 })
  ammoDown()
end

-- Skirmishers thrown out ahead: a tile in the block's colour, carrying its name.
function sendSkirmishers()
  local tint = self.getColorTint()
  local tile = spawnObject({ type = 'Custom_Tile', position = inFront(), rotation = self.getRotation(),
    scale = {x = 0.12, y = 1, z = 0.17}, snap_to_grid = false })
  tile.setCustomObject({
    image = "https://steamusercontent-a.akamaihd.net/ugc/1000000000000000002/89ABCDEF0123456789ABCDEF0123456789ABCDEF/",
    thickness = 0.125 })
  tile.setName(name)
  tile.setColorTint(tint)
end
