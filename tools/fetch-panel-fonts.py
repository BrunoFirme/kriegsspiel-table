import os
import sys
import tempfile
import urllib.request

SOURCE = "https://raw.githubusercontent.com/google/fonts/main/"
TARGET = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "App", "fonts", "panels")
TIMEOUT_SECONDS = 60
FONT_MAGIC = (b"\x00\x01\x00\x00", b"OTTO", b"true")
FAMILIES = {
    "ofl/bangers": ["Bangers-Regular.ttf"],
    "ofl/berkshireswash": ["BerkshireSwash-Regular.ttf"],
    "apache/cherrycreamsoda": ["CherryCreamSoda-Regular.ttf"],
    "ofl/creteround": ["CreteRound-Regular.ttf", "CreteRound-Italic.ttf"],
    "ofl/flamenco": ["Flamenco-Regular.ttf", "Flamenco-Light.ttf"],
    "ofl/griffy": ["Griffy-Regular.ttf"],
    "ofl/ibmplexsans": ["IBMPlexSans[wdth,wght].ttf", "IBMPlexSans-Italic[wdth,wght].ttf"],
    "ofl/lifesavers": ["LifeSavers-Regular.ttf", "LifeSavers-Bold.ttf"],
    "ofl/loversquarrel": ["LoversQuarrel-Regular.ttf"],
    "ofl/montserrat": ["Montserrat[wght].ttf", "Montserrat-Italic[wght].ttf"],
    "ofl/orbitron": ["Orbitron[wght].ttf"],
    "ofl/overlocksc": ["OverlockSC-Regular.ttf"],
    "ofl/sancreek": ["Sancreek-Regular.ttf"],
    "ofl/shojumaru": ["Shojumaru-Regular.ttf"],
    "ofl/sonsieone": ["SonsieOne-Regular.ttf"],
    "ofl/unifrakturcook": ["UnifrakturCook-Bold.ttf"],
}


def fetch(path):
    url = SOURCE + urllib.request.quote(path)
    with urllib.request.urlopen(url, timeout=TIMEOUT_SECONDS) as reply:
        return reply.read()


def keep(name, data):
    os.makedirs(TARGET, exist_ok=True)
    handle, temporary = tempfile.mkstemp(dir=TARGET, suffix=".part")
    with os.fdopen(handle, "wb") as out:
        out.write(data)
    os.replace(temporary, os.path.join(TARGET, name))


def licence(folder):
    name = "LICENSE.txt" if folder.startswith("apache/") else "OFL.txt"
    data = fetch(folder + "/" + name)
    keep(folder.split("/")[1] + "-" + name, data)


def main():
    failed = []
    for folder, files in FAMILIES.items():
        try:
            licence(folder)
            for name in files:
                data = fetch(folder + "/" + name)
                if not data.startswith(FONT_MAGIC):
                    raise ValueError(name + " is not a font file")
                keep(name, data)
                print("kept", name)
        except (OSError, ValueError) as e:
            failed.append(folder + ": " + str(e))
    for line in failed:
        print("failed", line, file=sys.stderr)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
