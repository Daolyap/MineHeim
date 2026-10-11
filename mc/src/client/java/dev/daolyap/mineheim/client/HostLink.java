package dev.daolyap.mineheim.client;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import dev.daolyap.mineheim.Creatures;
import dev.daolyap.mineheim.HostItems;
import dev.daolyap.mineheim.HostLights;
import dev.daolyap.mineheim.MineHeim;
import dev.daolyap.mineheim.NetherInfection;
import dev.daolyap.mineheim.WorldBridge;
import java.net.InetSocketAddress;
import java.util.Locale;
import net.minecraft.client.Minecraft;
import org.java_websocket.WebSocket;
import org.java_websocket.handshake.ClientHandshake;
import org.java_websocket.server.WebSocketServer;

/**
 * The host's connection: a WebSocket server on 127.0.0.1 (port 25599, or -Dmineheim.port).
 *
 * <p>Host to Minecraft (JSON, Minecraft coordinates):
 * <ul>
 * <li>{"t":"cam","f":frame,"p":[x,y,z],"r":[yaw,pitch,roll],"fov":vertical degrees,"fp":first person,"pl":[feet x,y,z],"h":body yaw}</li>
 * <li>{"t":"ground","c":[x,z,yBottom,yTop, ...],"r":[...]}: barrier columns to add ("c") and to remove again ("r")</li>
 * <li>{"t":"probe","c":[x,y,z, ...]}: reply {"t":"probe","r":[1 if that cell is a barrier else 0, ...]}</li>
 * <li>{"t":"clear"}: remove the barriers placed so far</li>
 * <li>{"t":"cmd","c":"time set noon"}: a server command</li>
 * <li>{"t":"key","k":"use|attack|pick|inventory|drop|swap|escape","down":bool}</li>
 * <li>{"t":"slot","n":0-8}, {"t":"scroll","d":+-1}, {"t":"hud","hidden":bool}, {"t":"view","w":px,"h":px}</li>
 * <li>{"t":"blocksync","r":radius}: report every solid block near the player as a "blocks" message</li>
 * <li>{"t":"projhit","id":entity id,"pos":[x,y,z],"stick":bool}: the host traced a projectile into something</li>
 * <li>{"t":"gamemode","m":"creative|survival"}: the host's choice (it owns health either way)</li>
 * <li>{"t":"vlights","c":[x,y,z,level,...],"r":[x,y,z,...]}: the host's fires as light blocks; {"t":"daytime","v":ticks}: its time of day</li>
 * <li>{"t":"itemin","item":id,"n":count,"pos":[x,y,z],"vel":[x,y,z]}: the host's items, thrown into this world</li>
 * <li>{"t":"itemtake","id":entity,"n":count}: the host takes part of a stack the player threw; {"t":"query"}: clock, items, inventory</li>
 * <li>{"t":"creatures","c":[[id,name,x,y,z,width,height],...]}: the host's creatures near its player (they stand here as CreatureProxy)</li>
 * <li>{"t":"mhit","id":mob,"amount":hp,"by":creature id,"player":bool}: the host hurt one of the mobs; {"t":"mobfight","on":bool}</li>
 * <li>{"t":"vstats","hp":0..1,"food":0..20}: the host's player's health and food, for Survival's hearts and hunger bar</li>
 * <li>{"t":"steve","on":bool,"body":bool}: Minecraft mode in the host (Steve, his hand and the HUD show; body false: the host draws Steve's
 * body itself); {"t":"interact"}: the host's "use" on a container</li>
 * <li>{"t":"skinreq","mode":"steve|account"}: send the skin the host should dress its own Steve in (SkinSender)</li>
 * <li>{"t":"heights","c":[x,z,cm,...]}: the host's exact ground per column (mobs and items are drawn standing on it)</li>
 * <li>{"t":"window","hidden":bool,"host":pid,"quitWithHost":bool}: park the window; follow the host process</li>
 * <li>{"t":"nethercfg","on":bool,"max":blocks,"grow":blocks/min,"heal":blocks/min,"mobs":cap,"every":s}: the Nether infection's settings;
 * {"t":"wards","w":[x,z,radius,...]}: the host's wards (nothing infects inside them); {"t":"nether","a":"clear|heal|grow"}</li>
 * </ul>
 * Minecraft to host: {"t":"hello",...} on connect, {"t":"ready"} when a Minecraft world has started (resend the ground), {"t":"explosion","pos":[x,y,z],"r":radius,"src":"tnt"},
 * {"t":"blocks","set":[x,y,z,...],"clear":[x,y,z,...]}, {"t":"proj","p":[[id,kind,x,y,z],...]},
 * {"t":"melee","item":...}, {"t":"pteleport","pos":[x,y,z]}, {"t":"shield","up":bool}, {"t":"hurt","amount":hp,"src":mob,"pos":[x,y,z]},
 * {"t":"mclights","set":[x,y,z,level,...],"clear":[x,y,z,...]}, {"t":"itemout","id":entity,"item":id,"n":count,"pos":[x,y,z]},
 * {"t":"itemtaken","id":entity,"item":id,"n":count,"pos":[x,y,z]}, {"t":"query",...},
 * {"t":"mobs","m":[[id,type,x,y,z,width,height,health,maxHealth,hostile],...]}, {"t":"chit","id":creature,"amount":hp,"src":mob,"by":mob id,"ranged":bool,"pos":[x,y,z]},
 * {"t":"skin","png":base64,"slim":bool,"src":"steve|account"}, {"t":"armswing","off":bool},
 * {"t":"audio","music":0-1,"master":0-1}: Minecraft's music and master volume (the host's Audio.* config),
 * {"t":"infect","l":[[id,x,y,z,radius,lit,columns,cells,waiting],...],"mobs":n}: the Nether infections (once a second when changed).
 */
public final class HostLink extends WebSocketServer {
	private static HostLink instance;

	private HostLink(final int port) {
		super(new InetSocketAddress("127.0.0.1", port));
		// on Windows SO_REUSEADDR lets a second Minecraft bind the same port and steal the host's messages: only elsewhere
		this.setReuseAddr(!System.getProperty("os.name", "").startsWith("Windows"));
		this.setDaemon(true);
	}

	static void launch() {
		int port = Integer.getInteger("mineheim.port", 25599);
		instance = new HostLink(port);
		instance.start();
		MineHeim.events = message -> instance.broadcast(message);
	}

	@Override
	public void onStart() {
		MineHeim.LOG.info("host link listening on 127.0.0.1:{}", this.getPort());
	}

	@Override
	public void onOpen(final WebSocket conn, final ClientHandshake handshake) {
		MineHeim.LOG.info("host connected from {}", conn.getRemoteSocketAddress());
		conn.send(String.format(Locale.ROOT, "{\"t\":\"hello\",\"v\":1,\"shm\":\"%s\",\"pid\":%d}", FrameExporter.NAME.replace("\\", "\\\\"), ProcessHandle.current().pid()));
	}

	@Override
	public void onClose(final WebSocket conn, final int code, final String reason, final boolean remote) {
		MineHeim.LOG.info("host disconnected ({} {})", code, reason);
		if (this.getConnections().isEmpty()) {
			Creatures.clear(); // the host's creatures leave with it
			HostView.clearHeights();
			// and Minecraft's own window is shown again (the host may have hidden it)
			Minecraft minecraft = Minecraft.getInstance();
			minecraft.execute(() -> WindowParking.set(minecraft.getWindow().handle(), false));
		}
	}

	@Override
	public void onMessage(final WebSocket conn, final String message) {
		try {
			JsonObject m = JsonParser.parseString(message).getAsJsonObject();
			switch (m.get("t").getAsString()) {
				case "cam" -> HostState.update(m);
				case "ground" -> {
					WorldBridge.solid(ints(m.getAsJsonArray("c")));
					if (m.has("r")) {
						WorldBridge.unsolid(ints(m.getAsJsonArray("r")));
					}
				}
				case "heights" -> {
					int[] c = ints(m.getAsJsonArray("c"));
					HostView.heights(c);
					WorldBridge.heights(c);
				}
				case "nethercfg" -> NetherInfection.configure(m);
				case "wards" -> NetherInfection.wards(m.getAsJsonArray("w"));
				case "nether" -> NetherInfection.command(m.get("a").getAsString());
				case "probe" -> WorldBridge.probe(ints(m.getAsJsonArray("c")));
				case "clear" -> WorldBridge.clearSolid();
				case "cmd" -> WorldBridge.command(m.get("c").getAsString());
				case "blocksync" -> WorldBridge.sync(m.has("r") ? m.get("r").getAsInt() : 48);
				case "gamemode" -> MineHeimClient.setGameMode(m.get("m").getAsString());
				case "vlights" -> HostLights.fires(ints(m.getAsJsonArray("c")), m.has("r") ? ints(m.getAsJsonArray("r")) : new int[0]);
				case "daytime" -> HostLights.dayTime(m.get("v").getAsInt());
				case "itemin" -> {
					JsonArray at = m.getAsJsonArray("pos");
					JsonArray vel = m.has("vel") ? m.getAsJsonArray("vel") : null;
					HostItems.arrive(m.get("item").getAsString(), m.get("n").getAsInt(), at.get(0).getAsDouble(), at.get(1).getAsDouble(), at.get(2).getAsDouble(),
						vel != null ? vel.get(0).getAsDouble() : 0, vel != null ? vel.get(1).getAsDouble() : 0, vel != null ? vel.get(2).getAsDouble() : 0);
				}
				case "itemtake" -> HostItems.take(m.get("id").getAsInt(), m.get("n").getAsInt());
				case "query" -> HostItems.query();
				case "creatures" -> Creatures.update(m.getAsJsonArray("c"));
				case "mhit" -> Creatures.hitMob(m.get("id").getAsInt(), m.get("amount").getAsFloat(), m.has("by") ? m.get("by").getAsInt() : -1,
					m.has("player") && m.get("player").getAsBoolean());
				case "mobfight" -> Creatures.fight = m.get("on").getAsBoolean();
				case "skinreq" -> SkinSender.request(m.has("mode") ? m.get("mode").getAsString() : "steve");
				case "vstats" -> MineHeimClient.vikingStats(m.get("hp").getAsFloat(), m.get("food").getAsInt());
				case "projhit" -> {
					JsonArray at = m.getAsJsonArray("pos");
					WorldBridge.projectileHit(m.get("id").getAsInt(), at.get(0).getAsDouble(), at.get(1).getAsDouble(), at.get(2).getAsDouble(),
						m.has("stick") && m.get("stick").getAsBoolean());
				}
				default -> {
					Minecraft minecraft = Minecraft.getInstance();
					minecraft.execute(() -> ClientInput.handle(minecraft, m));
				}
			}
		} catch (RuntimeException e) {
			MineHeim.LOG.warn("bad host message {}: {}", message.length() > 200 ? message.substring(0, 200) : message, e.toString());
		}
	}

	@Override
	public void onError(final WebSocket conn, final Exception e) {
		MineHeim.LOG.warn("host link error", e);
	}

	private static int[] ints(final JsonArray a) {
		int[] out = new int[a.size()];
		for (int i = 0; i < out.length; i++) {
			out[i] = a.get(i).getAsInt();
		}

		return out;
	}
}
