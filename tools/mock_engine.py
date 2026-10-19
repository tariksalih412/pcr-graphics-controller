"""Fake CasparCG for testing without the real server: prints every AMCP command and replies "202 OK".
  python tools/mock_engine.py          (port 5250)
  python tools/mock_engine.py 5300     (another port, so it can run next to CasparCG)
"""
import asyncio
import sys
import time

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 5250


async def handle(reader, writer):
    print("client connected:", writer.get_extra_info("peername"))
    while line := await reader.readline():
        print(f"{time.time():.3f}  RX: {line.decode('utf-8', 'replace').rstrip()}")
        writer.write(b"202 OK\r\n")
        await writer.drain()
    print("client disconnected")


async def main():
    server = await asyncio.start_server(handle, "0.0.0.0", PORT)
    print(f"mock CasparCG listening on {PORT} (Ctrl+C to stop)")
    async with server:
        await server.serve_forever()

try:
    asyncio.run(main())
except KeyboardInterrupt:
    pass
