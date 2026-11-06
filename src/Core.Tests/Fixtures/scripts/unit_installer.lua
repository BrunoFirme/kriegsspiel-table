-- A tool piece, written for these tests: its button puts a new unit block on the table in front of it and gives
-- the block a script of its own, written out below in full. Module makers ship tools like this one. It calls
-- setLuaScript for what it is for, a literal script into a piece it has just made, so the safety check must let
-- it through untouched, comments inside the literal and all.

BLOCK_SCRIPT = [[
-- The block's own script: its name on its face, kept with the save, and a box on the back to change it.
name = "New unit"

function none() end

function onSave() return JSON.encode({ name = name }) end

function onLoad(saved)
  if saved ~= nil and saved ~= "" then name = JSON.decode(saved).name end
  self.createButton({ click_function = "none", function_owner = self, label = name,
    position = {0, 0.2, -0.6}, width = 0, height = 0, font_size = 200 })
  self.createInput({ input_function = "rename", function_owner = self, value = name, label = "Unit",
    position = {0, 0, -0.6}, rotation = {0, 0, 180}, width = 600, height = 110, font_size = 85 })
end

function rename(obj, color, value)
  name = value
  self.editButton({ index = 0, label = name })
end
]]

blocks = 0

function onSave() return JSON.encode({ blocks = blocks }) end

function onLoad(saved)
  if saved ~= nil and saved ~= "" then blocks = JSON.decode(saved).blocks or 0 end
  self.createButton({ click_function = "placeBlock", function_owner = self, label = "Place a block",
    tooltip = "Puts a new unit block in front of this tool", position = {0, 0.2, 0},
    width = 900, height = 220, font_size = 120, color = {0.2, 0.2, 0.2, 1}, font_color = {1, 1, 1, 1} })
end

function placeBlock()
  local block = spawnObject({ type = "BlockSquare", position = self.positionToWorld({0, 0, -2}),
    rotation = self.getRotation(), scale = {x = 1, y = 0.4, z = 0.6} })
  block.setLuaScript(BLOCK_SCRIPT)
  blocks = blocks + 1
  block.setName("Unit " .. blocks)
  print("Block " .. blocks .. " placed")
end
