#!/bin/sh
set -eu

name=kriegsspiel-table
exe=KriegsspielTable.x86_64
here=$(cd "$(dirname "$0")" && pwd)
data_home=${XDG_DATA_HOME:-$HOME/.local/share}
target=$HOME/.local/opt/$name
bin_dir=$HOME/.local/bin
desktop=$data_home/applications/$name.desktop
icon=$data_home/icons/hicolor/256x256/apps/$name.png

refresh_menus() {
    update-desktop-database "$data_home/applications" >/dev/null 2>&1 || true
    gtk-update-icon-cache -q "$data_home/icons/hicolor" >/dev/null 2>&1 || true
}

if [ "${1:-}" = "--uninstall" ]; then
    rm -f "$bin_dir/$name" "$desktop" "$icon"
    if [ -f "$target/$exe" ]; then rm -rf "$target"; fi
    refresh_menus
    echo "Kriegsspiel Table removed. Your packs, scenarios, saves and settings stay in $data_home/KriegsspielTable."
    exit 0
fi

if [ ! -f "$here/$exe" ]; then
    echo "Run install.sh from inside the unpacked folder." >&2
    exit 1
fi

if [ "$here" != "$target" ]; then
    mkdir -p "$(dirname "$target")"
    rm -rf "$target.new"
    cp -R "$here" "$target.new"
    if [ -f "$target/$exe" ]; then rm -rf "$target"; fi
    mv "$target.new" "$target"
fi

chmod +x "$target/$exe" "$target/install.sh"
for converter in "$target"/data_*/Converter; do
    if [ -f "$converter" ]; then chmod +x "$converter"; fi
done

mkdir -p "$bin_dir" "$(dirname "$desktop")" "$(dirname "$icon")"
ln -sf "$target/$exe" "$bin_dir/$name"
cp "$target/icon.png" "$icon"
cat > "$desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Kriegsspiel Table
Comment=Tabletop for umpired wargames
Exec="$target/$exe"
Path=$target
Icon=$name
Terminal=false
Categories=Game;StrategyGame;
StartupWMClass=Kriegsspiel Table
EOF
refresh_menus

echo "Kriegsspiel Table installed in $target."
echo "Start it from your applications menu, or run: $bin_dir/$name"
echo "To remove it: $target/install.sh --uninstall"
