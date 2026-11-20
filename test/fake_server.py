"""Fake Paper/Velocity console for testing LumPanel (reads stdin commands like a real server)."""
import random, sys, threading, time

kind = sys.argv[1] if len(sys.argv) > 1 else "paper"
crash_after = float(sys.argv[2]) if len(sys.argv) > 2 else 0
players = []

def log(level, msg):
    print(f"[{time.strftime('%H:%M:%S')} {level}]: {msg}", flush=True)

def boot():
    if kind == "velocity":
        log("INFO", "Booting up Velocity 3.4.0...")
        time.sleep(1)
        log("INFO", "Listening on /0.0.0.0:25565")
        return
    for line in ["Starting minecraft server version 1.21.4", "Loading properties", "Preparing level \"world\"",
                 "[LuckPerms] Loading configuration...", "[LumCraftDelivery] HTTPS API bağlı"]:
        log("INFO", line); time.sleep(0.4)
    log("WARN", "Ticking entity took 112ms (Türkçe karakter testi: çğıöşü ÇĞİÖŞÜ)")
    log("INFO", "Done (3.214s)! For help, type \"help\"")

def life():
    names = ["Utku3", "Lum1vo", "Steve", "Kaplan_TR"]
    while True:
        time.sleep(random.uniform(1.5, 3))
        if kind == "velocity":
            continue
        if len(players) < 3:
            n = names[len(players)]
            players.append(n); log("INFO", f"{n} joined the game")
        if random.random() < 0.15:
            log("ERROR", "Could not pass event PlayerMoveEvent to SomePlugin")

boot()
threading.Thread(target=life, daemon=True).start()
if crash_after:
    def crash():
        time.sleep(crash_after); log("ERROR", "java.lang.OutOfMemoryError: Java heap space"); sys.stdout.flush()
        import os; os._exit(1)
    threading.Thread(target=crash, daemon=True).start()
for raw in sys.stdin:
    cmd = raw.strip()
    if not cmd:
        continue
    if cmd in ("stop", "end"):
        log("INFO", "Stopping server"); time.sleep(1); log("INFO", "Saving worlds"); time.sleep(0.5); break
    elif cmd in ("list", "glist"):
        log("INFO", f"There are {len(players)} of a max of 50 players online: {', '.join(players)}")
    elif cmd.startswith("say "):
        log("INFO", f"[Server] {cmd[4:]}")
    elif cmd.startswith("kick "):
        who = cmd.split()[1]
        if who in players: players.remove(who); log("INFO", f"{who} left the game")
    elif cmd == "tps":
        log("INFO", "TPS from last 1m, 5m, 15m: 20.0, 20.0, 19.98")
    else:
        log("INFO", f"Unknown or incomplete command: {cmd}")
