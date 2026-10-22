"""Phase 6 test helper: cut the AMCP connection without touching CasparCG.

    python tools/amcp_proxy.py            # listens on 5251, forwards to 127.0.0.1:5250
    dotnet run --project src/PcrGraphics.App -- 127.0.0.1 5251

Press Enter in this window to drop every open connection (CasparCG keeps its layers,
the app reconnects through the proxy and must re-sync). Type "hold" + Enter to drop
AND refuse new connections until you press Enter again. Ctrl+C quits.
"""
import socket
import sys
import threading

LISTEN_PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 5251
TARGET = ("127.0.0.1", int(sys.argv[2]) if len(sys.argv) > 2 else 5250)

lock = threading.Lock()
open_sockets: list[socket.socket] = []
holding = threading.Event()


def pipe(src: socket.socket, dst: socket.socket) -> None:
    try:
        while data := src.recv(65536):
            dst.sendall(data)
    except OSError:
        pass
    finally:
        for s in (src, dst):
            try:
                s.close()
            except OSError:
                pass


def serve() -> None:
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind(("127.0.0.1", LISTEN_PORT))
    server.listen()
    print(f"proxy 127.0.0.1:{LISTEN_PORT} -> {TARGET[0]}:{TARGET[1]}  (Enter = drop, hold = drop and refuse)")
    while True:
        client, addr = server.accept()
        if holding.is_set():
            client.close()
            continue
        try:
            upstream = socket.create_connection(TARGET, timeout=3)
            upstream.settimeout(None)
        except OSError as ex:
            print(f"  CasparCG not reachable: {ex}")
            client.close()
            continue
        with lock:
            open_sockets.extend([client, upstream])
        print(f"  connected {addr[0]}:{addr[1]}")
        threading.Thread(target=pipe, args=(client, upstream), daemon=True).start()
        threading.Thread(target=pipe, args=(upstream, client), daemon=True).start()


def drop_all() -> None:
    with lock:
        for s in open_sockets:
            try:
                s.shutdown(socket.SHUT_RDWR)
                s.close()
            except OSError:
                pass
        n = len(open_sockets) // 2
        open_sockets.clear()
    print(f"  dropped {n} connection(s)")


threading.Thread(target=serve, daemon=True).start()
try:
    for line in sys.stdin:
        if line.strip().lower() == "hold":
            holding.set()
            drop_all()
            print("  HOLD: refusing connections, press Enter to allow again")
        elif holding.is_set():
            holding.clear()
            print("  connections allowed")
        else:
            drop_all()
except KeyboardInterrupt:
    pass
