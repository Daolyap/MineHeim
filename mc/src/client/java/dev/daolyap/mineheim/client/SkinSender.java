package dev.daolyap.mineheim.client;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import dev.daolyap.mineheim.MineHeim;
import java.io.IOException;
import java.io.InputStream;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.Base64;
import java.util.Locale;
import java.util.Optional;
import java.util.UUID;
import net.minecraft.client.Minecraft;
import net.minecraft.resources.Identifier;
import net.minecraft.server.packs.resources.Resource;

/**
 * The host draws Steve itself (no frame of delay) and asks for his skin: {"t":"skinreq","mode":"steve|account"}.
 * The answer is the PNG, as Minecraft has it: {"t":"skin","png":base64,"slim":bool,"src":"steve|account","err":why}.
 * "steve" is the classic default from Minecraft's own resources; "account" is the signed-in account's skin from Mojang's
 * session server (on failure, Steve again with the reason). The account choice also dresses Minecraft's own model.
 */
public final class SkinSender {
	private static final Identifier STEVE = Identifier.withDefaultNamespace("textures/entity/player/wide/steve.png");
	private static final HttpClient HTTP = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(10))
		.followRedirects(HttpClient.Redirect.NORMAL).build();

	private SkinSender() {
	}

	static void request(final String mode, final boolean herobrine) {
		HerobrineSkin.wanted = herobrine;
		boolean account = "account".equals(mode);
		HostView.ownSkin = account;
		if (!account) {
			Minecraft minecraft = Minecraft.getInstance();
			minecraft.execute(() -> sendSteve(minecraft, null));
			return;
		}

		Thread thread = new Thread(SkinSender::fetchAccount, "MineHeim skin");
		thread.setDaemon(true);
		thread.start();
	}

	private static void sendSteve(final Minecraft minecraft, final String error) {
		try {
			Optional<Resource> resource = minecraft.getResourceManager().getResource(STEVE);
			if (resource.isEmpty()) {
				send(null, false, "steve", "no " + STEVE + " in Minecraft's resources");
				return;
			}

			try (InputStream in = resource.get().open()) {
				send(in.readAllBytes(), false, "steve", error);
			}
		} catch (IOException e) {
			send(null, false, "steve", e.toString());
		}
	}

	/** Mojang's session server: the profile's "textures" property names the skin's URL and whether its arms are slim. */
	private static void fetchAccount() {
		Minecraft minecraft = Minecraft.getInstance();
		try {
			UUID id = minecraft.getUser().getProfileId();
			String profile = get("https://sessionserver.mojang.com/session/minecraft/profile/" + id.toString().replace("-", ""));
			String textures = null;
			for (var property : JsonParser.parseString(profile).getAsJsonObject().getAsJsonArray("properties")) {
				JsonObject p = property.getAsJsonObject();
				if ("textures".equals(p.get("name").getAsString())) {
					textures = new String(Base64.getDecoder().decode(p.get("value").getAsString()), StandardCharsets.UTF_8);
				}
			}

			JsonObject skin = textures == null ? null : JsonParser.parseString(textures).getAsJsonObject().getAsJsonObject("textures").getAsJsonObject("SKIN");
			if (skin == null) {
				minecraft.execute(() -> sendSteve(minecraft, "the account has no skin of its own"));
				return;
			}

			boolean slim = skin.has("metadata") && "slim".equals(skin.getAsJsonObject("metadata").get("model").getAsString());
			String url = skin.get("url").getAsString().replaceFirst("^http:", "https:");
			HttpResponse<byte[]> png = HTTP.send(HttpRequest.newBuilder(URI.create(url)).timeout(Duration.ofSeconds(15)).build(), HttpResponse.BodyHandlers.ofByteArray());
			if (png.statusCode() != 200) {
				throw new IOException("skin download: HTTP " + png.statusCode());
			}

			send(png.body(), slim, "account", null);
		} catch (IOException | RuntimeException e) {
			MineHeim.LOG.warn("could not fetch the account's skin, using Steve: {}", e.toString());
			minecraft.execute(() -> sendSteve(minecraft, "account skin: " + e));
		} catch (InterruptedException e) {
			Thread.currentThread().interrupt();
		}
	}

	private static String get(final String url) throws IOException, InterruptedException {
		HttpResponse<String> r = HTTP.send(HttpRequest.newBuilder(URI.create(url)).timeout(Duration.ofSeconds(15)).build(), HttpResponse.BodyHandlers.ofString());
		if (r.statusCode() != 200) {
			throw new IOException("session server: HTTP " + r.statusCode());
		}

		return r.body();
	}

	private static void send(final byte[] png, final boolean slim, final String source, final String error) {
		JsonObject m = new JsonObject();
		m.addProperty("t", "skin");
		if (png != null) {
			m.addProperty("png", Base64.getEncoder().encodeToString(png));
		}

		m.addProperty("slim", slim);
		m.addProperty("src", source);
		if (error != null) {
			m.addProperty("err", error.replace('"', '\''));
		}

		MineHeim.LOG.info("skin for the host: {} ({} bytes{})", source, png == null ? 0 : png.length, error == null ? "" : ", " + error);
		MineHeim.events.accept(m.toString());
		HerobrineSkin.make(png); // Minecraft draws him in the same skin: with the same eyes
	}

	/** The local player swung an arm (attack, mining, placing, using): the host's Steve swings too. */
	public static void armSwing(final boolean offHand) {
		MineHeim.events.accept(String.format(Locale.ROOT, "{\"t\":\"armswing\",\"off\":%b}", offHand));
	}
}
