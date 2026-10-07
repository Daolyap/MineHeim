package dev.daolyap.mineheim.client;

import dev.daolyap.mineheim.MineHeim;
import java.lang.foreign.FunctionDescriptor;
import java.lang.foreign.Linker;
import java.lang.foreign.SymbolLookup;
import java.lang.foreign.ValueLayout;
import java.lang.invoke.MethodHandle;
import org.lwjgl.sdl.SDLProperties;
import org.lwjgl.sdl.SDLVideo;

/**
 * Minecraft's own window out of the way while a host shows its picture: sent behind every other window and taken out of
 * the taskbar and Alt-Tab (a tool window). Not hidden, minimised or moved off the screen: Minecraft stops rendering then. Windows only; anywhere
 * else, or if a call fails, the window simply stays where it is.
 */
final class WindowParking {
	private static final int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
	private static boolean parked;
	private static long savedStyle;
	private static final long HWND_BOTTOM = 1L;
	private static final int SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
	private static MethodHandle getLong, setLong, show, setPos;

	private WindowParking() {
	}

	private static boolean bind() {
		if (getLong != null) {
			return true;
		}

		try {
			Linker linker = Linker.nativeLinker();
			SymbolLookup user32 = SymbolLookup.libraryLookup("user32", java.lang.foreign.Arena.global());
			getLong = linker.downcallHandle(user32.find("GetWindowLongPtrW").orElseThrow(),
				FunctionDescriptor.of(ValueLayout.JAVA_LONG, ValueLayout.JAVA_LONG, ValueLayout.JAVA_INT));
			setLong = linker.downcallHandle(user32.find("SetWindowLongPtrW").orElseThrow(),
				FunctionDescriptor.of(ValueLayout.JAVA_LONG, ValueLayout.JAVA_LONG, ValueLayout.JAVA_INT, ValueLayout.JAVA_LONG));
			show = linker.downcallHandle(user32.find("ShowWindow").orElseThrow(),
				FunctionDescriptor.of(ValueLayout.JAVA_INT, ValueLayout.JAVA_LONG, ValueLayout.JAVA_INT));
			setPos = linker.downcallHandle(user32.find("SetWindowPos").orElseThrow(), FunctionDescriptor.of(ValueLayout.JAVA_INT, ValueLayout.JAVA_LONG,
				ValueLayout.JAVA_LONG, ValueLayout.JAVA_INT, ValueLayout.JAVA_INT, ValueLayout.JAVA_INT, ValueLayout.JAVA_INT, ValueLayout.JAVA_INT));
			return true;
		} catch (Throwable e) {
			MineHeim.LOG.warn("window parking unavailable: {}", e.toString());
			return false;
		}
	}

	private static long hwnd(final long window) {
		return SDLProperties.SDL_GetPointerProperty(SDLVideo.SDL_GetWindowProperties(window), SDLVideo.SDL_PROP_WINDOW_WIN32_HWND_POINTER, 0L);
	}

	/** Render thread (client): park or bring back the window. */
	static void set(final long window, final boolean park) {
		if (park == parked || !System.getProperty("os.name", "").startsWith("Windows") || !bind()) {
			return;
		}

		try {
			long hwnd = hwnd(window);
			if (hwnd == 0L) {
				return;
			}

			if (park) {
				savedStyle = (long) getLong.invokeExact(hwnd, GWL_EXSTYLE);
				// the taskbar only notices a changed tool-window style when the window is shown again
				int unused = (int) show.invokeExact(hwnd, SW_HIDE);
				long previous = (long) setLong.invokeExact(hwnd, GWL_EXSTYLE, (savedStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
				unused = (int) show.invokeExact(hwnd, SW_SHOWNOACTIVATE);
				unused = (int) setPos.invokeExact(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
			} else {
				int unused = (int) show.invokeExact(hwnd, SW_HIDE);
				long previous = (long) setLong.invokeExact(hwnd, GWL_EXSTYLE, savedStyle);
				unused = (int) show.invokeExact(hwnd, SW_SHOWNOACTIVATE);
			}

			parked = park;
			MineHeim.LOG.info("minecraft window {}", park ? "parked behind the other windows (out of the taskbar and Alt-Tab)" : "back");
		} catch (Throwable e) {
			MineHeim.LOG.warn("window parking failed: {}", e.toString());
		}
	}
}
