"""Send raw JSON messages to the mineheim mod, as the host would: python send.py '{"t":"key","k":"drop","down":true}' ..."""
import asyncio, os, sys
import websockets

async def main(messages):
    async with websockets.connect(f"ws://127.0.0.1:{os.environ.get('MINEHEIM_PORT', '25599')}") as ws:
        await ws.recv()
        for m in messages:
            await ws.send(m)
        await asyncio.sleep(0.5)

asyncio.run(main(sys.argv[1:]))
