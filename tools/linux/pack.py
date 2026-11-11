import io
import os
import sys
import tarfile

RUNNABLE = {"KriegsspielTable.x86_64", "Converter", "install.sh"}


def mode_for(name):
    return 0o755 if name in RUNNABLE or name.endswith(".so") else 0o644


def add_file(tar, path, arcname):
    with open(path, "rb") as f:
        data = f.read()
    if arcname.endswith(".sh") or (arcname.count("/") == 1 and arcname.endswith(".txt")):
        data = data.replace(b"\r\n", b"\n")
    info = tarfile.TarInfo(arcname)
    info.size = len(data)
    info.mode = mode_for(os.path.basename(arcname))
    info.mtime = int(os.path.getmtime(path))
    tar.addfile(info, io.BytesIO(data))


def add_folder(tar, arcname, mtime):
    info = tarfile.TarInfo(arcname)
    info.type = tarfile.DIRTYPE
    info.mode = 0o755
    info.mtime = mtime
    tar.addfile(info)


def main(dist, out, top):
    with tarfile.open(out, "w:gz", format=tarfile.GNU_FORMAT, compresslevel=9) as tar:
        add_folder(tar, top, int(os.path.getmtime(dist)))
        for folder, dirs, files in os.walk(dist):
            dirs.sort()
            rel = os.path.relpath(folder, dist)
            for d in dirs:
                add_folder(tar, "/".join(p for p in (top, rel, d) if p != "."), int(os.path.getmtime(os.path.join(folder, d))))
            for f in sorted(files):
                add_file(tar, os.path.join(folder, f), "/".join(p for p in (top, rel, f) if p != "."))
    runnable = []
    with tarfile.open(out, "r:gz") as tar:
        for m in tar.getmembers():
            if m.isfile() and os.path.basename(m.name) in RUNNABLE:
                runnable.append(m.name)
    missing = RUNNABLE - {os.path.basename(n) for n in runnable}
    if missing:
        sys.exit("the archive lacks " + ", ".join(sorted(missing)))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3])
