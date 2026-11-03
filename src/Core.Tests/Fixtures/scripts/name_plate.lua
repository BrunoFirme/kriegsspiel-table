-- A regiment's name plate, written for these tests the way Tabletop Simulator module pieces are written: three
-- lines printed on the face (regiment, division, brigade) and a box for each on the back to type them in. Each
-- line is set with `line = value, self.editButton{...}`, as such pieces do, so the face shows a line one edit late.

REGIMENT_TEXT, DIVISION_TEXT, BRIGADE_TEXT = 'Black', 'White', 'White'

regiment, division, brigade = "", "", ""

function none() end

function onSave()
  return JSON.encode({ regiment = regiment, division = division, brigade = brigade })
end

function onLoad(saved)
  if saved ~= nil and saved ~= "" then
    local lines = JSON.decode(saved)
    regiment, division, brigade = lines.regiment, lines.division, lines.brigade
  end
  makeButtons()
end

-- Buttons 0-2 print the lines on the face; the inputs that edit them are on the back, in the same order.
function makeButtons()
  self.createButton({ click_function = "none", function_owner = self, label = regiment,
    position = {0, 0.1, -0.71}, width = 0, height = 0, font_size = 200, font_color = REGIMENT_TEXT })
  self.createButton({ click_function = "none", function_owner = self, label = division,
    position = {0.9, 0.1, 0.27}, width = 0, height = 0, font_size = 175, font_color = DIVISION_TEXT })
  self.createButton({ click_function = "none", function_owner = self, label = brigade,
    position = {0.3, 0.1, 0.8}, width = 0, height = 0, font_size = 150, font_color = BRIGADE_TEXT })

  self.createInput({ input_function = "editRegiment", function_owner = self, value = regiment, label = "Regiment",
    tooltip = "Edit", alignment = 3, position = {0, 0, -0.73}, rotation = {0, 0, 180}, scale = {x = 1.5, y = 1.5, z = 1.5},
    width = 800, height = 110, font_size = 85, font_color = {0, 0, 0, 1}, color = {1, 0.8, 0, 1} })
  self.createInput({ input_function = "editDivision", function_owner = self, value = division, label = "Division",
    tooltip = "Edit", alignment = 3, position = {-0.85, 0, 0}, rotation = {0, 0, 180}, scale = {x = 1.5, y = 1.5, z = 1.5},
    width = 270, height = 110, font_size = 85, font_color = {1, 1, 1, 1}, color = {0.4, 0.4, 0.4, 1} })
  self.createInput({ input_function = "editBrigade", function_owner = self, value = brigade, label = "Brigade",
    tooltip = "Edit", alignment = 3, position = {0, 0, 0.73}, rotation = {0, 0, 180}, scale = {x = 1.5, y = 1.5, z = 1.5},
    width = 800, height = 110, font_size = 85, font_color = {0, 0, 0, 1}, color = {1, 0.8, 0, 1} })
end

function editRegiment(obj, color, value)
  regiment = value,
  self.editButton({index = 0, label = regiment})
end

function editDivision(obj, color, value)
  division = value,
  self.editButton({index = 1, label = division})
end

function editBrigade(obj, color, value)
  brigade = value,
  self.editButton({index = 2, label = brigade})
end
