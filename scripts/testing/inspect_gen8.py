#!/usr/bin/env python3
"""Read archive headers without modifying proprietary archives; offsets verified against UTMT serializer."""
import argparse, hashlib, json, struct
from pathlib import Path

def inspect(path):
    path = Path(path)
    data = path.read_bytes()
    if data[:4] != b"FORM":
        raise ValueError(f"Not a GameMaker FORM archive: {path}")
    chunks = []
    position = 8
    gen8 = None
    while position + 8 <= len(data):
        name = data[position:position+4].decode("ascii", errors="replace")
        size = struct.unpack_from("<I", data, position+4)[0]
        end = position+8+size
        if end > len(data):
            raise ValueError(f"Chunk {name} exceeds file length")
        chunks.append({"name": name, "offset": position, "size": size})
        if name == "GEN8":
            start = position+8
            version = struct.unpack_from("<4I", data, start+44)
            gen8 = {"bytecode": data[start+1], "engine_header": list(version),
                    "window": list(struct.unpack_from("<2I", data, start+60))}
        position = end
    return {"path": str(path.resolve()), "bytes": len(data),
            "sha256": hashlib.sha256(data).hexdigest(),
            "md5": hashlib.md5(data).hexdigest(), "GEN8": gen8, "chunks": chunks}

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("archives", nargs="+")
    args = parser.parse_args()
    print(json.dumps([inspect(p) for p in args.archives], indent=2))
