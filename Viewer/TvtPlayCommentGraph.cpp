#include "stdafx.h"
#include "TvtPlayCommentGraph.h"
#include <algorithm>
#include <cmath>
#include <commctrl.h>
#include <codecvt>
#include <locale>

#pragma comment(lib, "comctl32.lib")

namespace
{
	const wchar_t DISPATCH_CLASS[] = L"TVTComment.Activity.Dispatch";
	const wchar_t OVERLAY_CLASS[] = L"TVTComment.Activity.Overlay";
	const UINT WM_ACTIVITY_MESSAGE = WM_APP + 1;
	const UINT WM_ACTIVITY_COLORS = WM_APP + 2;
	const int ACTIVITY_INTERVAL_MS = 10000;
	const DWORD INITIAL_DISPLAY_MS = 10000;
	const UINT WM_TVTP_GET_MSGVER = WM_APP + 50;
	const UINT WM_TVTP_IS_OPEN = WM_APP + 51;
	const UINT WM_TVTP_GET_POSITION = WM_APP + 52;
	const UINT WM_TVTP_GET_DURATION = WM_APP + 53;
	const UINT WM_TVTP_GET_TOT_TIME = WM_APP + 54;
	const UINT WM_TVTP_IS_EXTENDING = WM_APP + 55;
	const UINT WM_TVTP_GET_PATH = WM_APP + 59;
	const UINT WM_TVTP_GET_TOT_UNIX = WM_APP + 62;

	HWND FindProcessWindow(LPCWSTR className, bool visibleOnly = false)
	{
		HWND window = nullptr;
		while ((window = FindWindowEx(nullptr, window, className, nullptr)) != nullptr) {
			DWORD process;
			GetWindowThreadProcessId(window, &process);
			if (process == GetCurrentProcessId() && (!visibleOnly || IsWindowVisible(window))) return window;
		}
		return nullptr;
	}

	BOOL CALLBACK FindHostStatus(HWND hwnd, LPARAM parameter)
	{
		wchar_t name[64];
		if (GetClassName(hwnd, name, _countof(name)) && !wcscmp(name, L"TVTest Status")) {
			*reinterpret_cast<HWND *>(parameter) = hwnd;
			return FALSE;
		}
		return TRUE;
	}

	std::wstring Wide(const std::string &text)
	{
		return std::wstring_convert<std::codecvt_utf8_utf16<wchar_t>>().from_bytes(text);
	}

	int Integer(const std::string &text)
	{
		if (text.empty() || text.find_first_not_of("0123456789") != std::string::npos) throw std::invalid_argument("Invalid activity count");
		return std::stoi(text);
	}

	int IntervalCount(int duration) { return static_cast<int>((duration + ACTIVITY_INTERVAL_MS - 1LL) / ACTIVITY_INTERVAL_MS); }

	COLORREF Mix(COLORREF a, COLORREF b, int weight)
	{
		auto channel = [weight](int x, int y) { return (x * (255 - weight) + y * weight + 127) / 255; };
		return RGB(channel(GetRValue(a), GetRValue(b)), channel(GetGValue(a), GetGValue(b)), channel(GetBValue(a), GetBValue(b)));
	}

	double Luminance(COLORREF color)
	{
		auto linear = [](int value) {
			double c = value / 255.0;
			return c <= 0.04045 ? c / 12.92 : std::pow((c + 0.055) / 1.055, 2.4);
		};
		return 0.2126 * linear(GetRValue(color)) + 0.7152 * linear(GetGValue(color)) + 0.0722 * linear(GetBValue(color));
	}

	COLORREF Readable(COLORREF ink, COLORREF background, int opacity, double minimum)
	{
		constexpr COLORREF black = RGB(0, 0, 0), white = RGB(255, 255, 255);
		// 映像の明暗とオーバーレイの透過を加味した最小コントラスト
		const double dark = Luminance(Mix(black, background, opacity)) + 0.05;
		const double light = Luminance(Mix(white, background, opacity)) + 0.05;
		auto contrast = [&](COLORREF color) {
			return max((Luminance(Mix(black, color, opacity)) + 0.05) / light,
				dark / (Luminance(Mix(white, color, opacity)) + 0.05));
		};
		if (contrast(ink) >= minimum) return ink;
		COLORREF end = contrast(black) > contrast(white) ? black : white;
		for (int weight = 16; weight < 255; weight += 16) {
			COLORREF adjusted = Mix(ink, end, weight);
			if (contrast(adjusted) >= minimum) return adjusted;
		}
		return end;
	}
}

bool TvtPlayCommentGraph::Initialize(HINSTANCE instance, TVTest::CTVTestApp *app, Sender sender, Clock clock, const std::wstring &iniPath)
{
	instance_ = instance;
	app_ = app;
	sender_ = std::move(sender);
	clock_ = std::move(clock);
	iniPath_ = iniPath;
	enabled_ = GetPrivateProfileInt(L"Setting", L"commentActivityGraph", 1, iniPath_.c_str()) != 0;
	height_ = max(40, min(240, static_cast<int>(GetPrivateProfileInt(L"Setting", L"commentActivityGraphHeight", 72, iniPath_.c_str()))));
	opacity_ = max(32, min(255, static_cast<int>(GetPrivateProfileInt(L"Setting", L"commentActivityGraphOpacity", 220, iniPath_.c_str()))));
	UpdateColors();
	WNDCLASSEX wc{};
	wc.cbSize = sizeof(wc);
	wc.hInstance = instance_;
	wc.lpfnWndProc = WindowProc;
	wc.lpszClassName = DISPATCH_CLASS;
	if (!RegisterClassEx(&wc)) return false;
	wc.lpszClassName = OVERLAY_CLASS;
	if (!RegisterClassEx(&wc)) { Finalize(); return false; }
	dispatcher_ = CreateWindowEx(0, DISPATCH_CLASS, L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, instance_, this);
	if (!dispatcher_) { Finalize(); return false; }
	TVTest::PluginCommandInfo command{};
	command.Size = sizeof(command);
	command.ID = CommandId;
	command.State = TVTest::PLUGIN_COMMAND_STATE_DISABLED;
	command.pszText = L"ToggleCommentActivityGraph";
	command.pszName = command.pszDescription = L"TvtPlayの盛り上がりグラフ";
	if (!app_->RegisterPluginCommand(&command)) app_->RegisterCommand(command.ID, command.pszText, command.pszName);
	return true;
}

void TvtPlayCommentGraph::OnColorChange()
{
	// 配色プレビューの適用・取り消し完了後の再取得
	if (dispatcher_) PostMessage(dispatcher_, WM_ACTIVITY_COLORS, 0, 0);
}

void TvtPlayCommentGraph::UpdateColors()
{
	auto color = [&](LPCWSTR name, COLORREF fallback) {
		COLORREF c = app_->GetColor(name);
		return c == CLR_INVALID ? fallback : c;
	};
	COLORREF back = color(L"StatusBack", GetSysColor(COLOR_BTNFACE));
	colors_.background = Mix(back, color(L"StatusBack2", back), 128);
	auto ink = [&](COLORREF c, double minimum) { return Readable(c, colors_.background, opacity_, minimum); };
	colors_.text = ink(color(L"StatusText", GetSysColor(COLOR_BTNTEXT)), 4.5);
	colors_.line = ink(color(L"StatusHighlightBack", GetSysColor(COLOR_HIGHLIGHT)), 3.0);
	colors_.playhead = ink(color(L"StatusEventProgressElapsed", colors_.text), 3.0);
	colors_.grid = ink(Mix(colors_.background, colors_.text, 80), 1.5);
	colors_.error = ink(color(L"StatusRecordingCircle", colors_.text), 4.5);
	if (overlay_ && IsWindowVisible(overlay_)) Render();
}

void TvtPlayCommentGraph::Finalize()
{
	Stop();
	if (overlay_) DestroyWindow(overlay_);
	overlay_ = nullptr;
	{
		std::lock_guard<std::mutex> lock(mutex_);
		accepting_ = false;
		if (dispatcher_) DestroyWindow(dispatcher_);
		dispatcher_ = nullptr;
		pending_.clear();
	}
	if (instance_) {
		UnregisterClass(OVERLAY_CLASS, instance_);
		UnregisterClass(DISPATCH_CLASS, instance_);
	}
	sender_ = {};
}

void TvtPlayCommentGraph::SetPluginEnabled(bool enabled)
{
	pluginEnabled_ = enabled;
	ready_ = false;
	{
		std::lock_guard<std::mutex> lock(mutex_);
		accepting_ = enabled;
		pending_.clear();
	}
	if (!dispatcher_) return;
	if (enabled) SetTimer(dispatcher_, 1, 500, nullptr);
	else { KillTimer(dispatcher_, 1); Stop(); }
	app_->SetPluginCommandState(CommandId, !enabled ? TVTest::PLUGIN_COMMAND_STATE_DISABLED : enabled_ ? TVTest::COMMAND_ICON_STATE_CHECKED : 0);
}

bool TvtPlayCommentGraph::OnCommand(int command)
{
	if (command != CommandId) return false;
	SetEnabled(!enabled_);
	return true;
}

void TvtPlayCommentGraph::SetEnabled(bool enabled)
{
	if (enabled_ != enabled) {
		enabled_ = enabled;
		WritePrivateProfileString(L"Setting", L"commentActivityGraph", enabled_ ? L"1" : L"0", iniPath_.c_str());
		app_->SetPluginCommandState(CommandId, !pluginEnabled_ ? TVTest::PLUGIN_COMMAND_STATE_DISABLED : enabled_ ? TVTest::COMMAND_ICON_STATE_CHECKED : 0);
		if (!enabled_) Stop();
		else Tick();
	}
	if (ready_) Send("ActivitySettings", { enabled_ ? "1" : "0" });
}

bool TvtPlayCommentGraph::Send(const char *operation, std::vector<std::string> arguments)
{
	TVTComment::PanelIPCMessage message;
	message.Operation = operation;
	message.Arguments = std::move(arguments);
	return sender_ && sender_(message);
}

void TvtPlayCommentGraph::OnMessage(const TVTComment::IIPCMessage &message)
{
	auto panel = dynamic_cast<const TVTComment::PanelIPCMessage *>(&message);
	if (!panel || panel->Operation.compare(0, 8, "Activity") != 0) return;
	std::lock_guard<std::mutex> lock(mutex_);
	if (accepting_ && dispatcher_) {
		pending_.push_back(*panel);
		if (pending_.size() == 1) PostMessage(dispatcher_, WM_ACTIVITY_MESSAGE, 0, 0);
	}
}

void TvtPlayCommentGraph::Receive()
{
	std::deque<TVTComment::PanelIPCMessage> messages;
	{
		std::lock_guard<std::mutex> lock(mutex_);
		messages.swap(pending_);
	}
	for (const auto &message : messages) {
		const auto &args = message.Arguments;
		if (message.Operation == "ActivityReady") {
			ready_ = true;
			requestSent_ = false;
			Send("ActivitySettings", { enabled_ ? "1" : "0" });
			continue;
		}
		if (message.Operation == "ActivitySetEnabled" && args.size() == 1 && (args[0] == "0" || args[0] == "1")) {
			SetEnabled(args[0] == "1");
			continue;
		}
		if (args.empty() || args[0] != requestId_ || !enabled_ || !pluginEnabled_) continue;
		try {
			if (message.Operation == "ActivityReset" && args.size() == 2) {
				int count = Integer(args[1]);
				if (count == IntervalCount(requestedDuration_)) ResetCounts(count);
			}
			else if (message.Operation == "ActivityChunk" && args.size() >= 3 && args.size() <= 62) {
				int offset = Integer(args[1]);
				if (static_cast<size_t>(offset) + args.size() - 2 > counts_.size()) continue;
				std::vector<int> values;
				for (size_t i = 2; i < args.size(); ++i) values.push_back(Integer(args[i]));
				std::copy(values.begin(), values.end(), counts_.begin() + offset);
			}
			else if (message.Operation == "ActivityStatus" && args.size() == 3) {
				information_ = Wide(args[2]);
				if (args[1] == "error" && state_ != "error") app_->AddLog((L"盛り上がりグラフ: " + information_).c_str());
				state_ = args[1];
			}
		}
		catch (const std::exception &) {
			state_ = "error";
			information_ = L"グラフの集計結果を読み込めませんでした。";
		}
	}
	if (overlay_ && IsWindowVisible(overlay_)) Render();
}

void TvtPlayCommentGraph::Stop()
{
	if (requestSent_) Send("ActivityStop");
	Hide();
	AttachStatus(nullptr);
	path_.clear();
	start_ = 0;
	requestSent_ = false;
	requestId_.clear();
	counts_.clear();
	duration_ = requestedDuration_ = 0;
	cachedBar_ = {};
	cachedSize_ = {};
}

void TvtPlayCommentGraph::Hide()
{
	if (overlay_) ShowWindow(overlay_, SW_HIDE);
	ClearCommentClip();
}

void TvtPlayCommentGraph::ClearCommentClip()
{
	HWND window = commentWindow_;
	commentWindow_ = nullptr;
	if (IsWindow(window)) {
		RemoveWindowSubclass(window, CommentSubclass, reinterpret_cast<UINT_PTR>(this));
		if (!IsRectEmpty(&commentClip_)) SetWindowRgn(window, nullptr, TRUE);
	}
	commentClip_ = {};
	commentSize_ = {};
}

void TvtPlayCommentGraph::UpdateCommentClip()
{
	if (clipping_) return;
	if (!overlay_ || !IsWindowVisible(overlay_)) { ClearCommentClip(); return; }
	if (!IsWindow(commentWindow_)) {
		ClearCommentClip();
		HWND window = nullptr;
		while ((window = FindWindowEx(nullptr, window, L"ru.jk.comment", nullptr)) != nullptr) {
			if (GetWindowThreadProcessId(window, nullptr) == GetCurrentThreadId()
				&& GetWindowLongPtr(window, GWLP_HINSTANCE) == reinterpret_cast<LONG_PTR>(instance_)) {
				if (SetWindowSubclass(window, CommentSubclass, reinterpret_cast<UINT_PTR>(this), reinterpret_cast<DWORD_PTR>(this)))
					commentWindow_ = window;
				break;
			}
		}
	}
	RECT window, graph, clip{};
	if (!commentWindow_ || !GetWindowRect(commentWindow_, &window) || !GetWindowRect(overlay_, &graph)) return;
	if (IntersectRect(&clip, &window, &graph)) OffsetRect(&clip, -window.left, -window.top);
	SIZE size{ window.right - window.left, window.bottom - window.top };
	if (EqualRect(&clip, &commentClip_) && size.cx == commentSize_.cx && size.cy == commentSize_.cy) return;
	clipping_ = true;
	struct Reset { bool &flag; ~Reset() { flag = false; } } reset{ clipping_ };
	// 子コントロールのグラフを覆うコメントレイヤー領域の除外
	HRGN region = nullptr;
	if (!IsRectEmpty(&clip)) {
		region = CreateRectRgn(0, 0, size.cx, size.cy);
		HRGN hole = CreateRectRgnIndirect(&clip);
		if (!region || !hole || CombineRgn(region, region, hole, RGN_DIFF) == ERROR) {
			if (region) DeleteObject(region);
			if (hole) DeleteObject(hole);
			return;
		}
		DeleteObject(hole);
	}
	if (!SetWindowRgn(commentWindow_, region, TRUE)) {
		if (region) DeleteObject(region);
		return;
	}
	commentClip_ = clip;
	commentSize_ = size;
}

LRESULT CALLBACK TvtPlayCommentGraph::CommentSubclass(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR id, DWORD_PTR data)
{
	LRESULT result = DefSubclassProc(hwnd, message, wParam, lParam);
	auto self = reinterpret_cast<TvtPlayCommentGraph *>(data);
	if (message == WM_NCDESTROY) {
		RemoveWindowSubclass(hwnd, CommentSubclass, id);
		self->commentWindow_ = nullptr;
		self->commentClip_ = {};
		self->commentSize_ = {};
	}
	else if (message == WM_WINDOWPOSCHANGED || message == WM_SHOWWINDOW) self->UpdateCommentClip();
	return result;
}

LONGLONG TvtPlayCommentGraph::ResolveStartTime(int firstTime, LONGLONG currentUnixMilliseconds, int position)
{
	if (firstTime < 0 || firstTime >= 86400000 || currentUnixMilliseconds <= 0 || position < 0) return 0;
	const LONGLONG day = 86400000, japan = 32400000;
	LONGLONG approximate = currentUnixMilliseconds - position;
	LONGLONG start = (approximate + japan) / day * day + firstTime - japan;
	if (start - approximate > day / 2) start -= day;
	if (approximate - start > day / 2) start += day;
	return start;
}

void TvtPlayCommentGraph::ResetCounts(int count)
{
	counts_.assign(count, -1);
	state_ = "loading";
	information_ = L"録画全体のコメントを集計中…";
}

void TvtPlayCommentGraph::SendRequest()
{
	if (!ready_ || !start_ || duration_ <= 0) return;
	if (requestSent_ && duration_ == requestedDuration_) return;
	int count = IntervalCount(duration_);
	// 追っかけ再生中の10秒単位の末尾更新と、録画終了後の端数区間の確定
	if (requestSent_ && duration_ > requestedDuration_ && duration_ - requestedDuration_ < ACTIVITY_INTERVAL_MS
		&& count == IntervalCount(requestedDuration_)
		&& SendMessage(frame_, WM_TVTP_IS_EXTENDING, 0, 0)) return;
	requestedDuration_ = duration_;
	requestId_ = recordingId_ + ":" + std::to_string(++revision_);
	ResetCounts(count);
	requestSent_ = Send("ActivityRequest", { requestId_, recordingId_, std::to_string(start_), std::to_string(requestedDuration_),
		std::to_string(network_), std::to_string(transport_), std::to_string(service_) });
}

void TvtPlayCommentGraph::Tick()
{
	if (!pluginEnabled_ || !enabled_) { Hide(); return; }
	if (!IsWindow(frame_)) frame_ = FindProcessWindow(L"TvtPlay Frame");
	if (!frame_ || SendMessage(frame_, WM_TVTP_GET_MSGVER, 0, 0) < 1 || !SendMessage(frame_, WM_TVTP_IS_OPEN, 0, 0)) {
		Stop();
		return;
	}
	wchar_t path[32768];
	if (!SendMessage(frame_, WM_TVTP_GET_PATH, _countof(path), reinterpret_cast<LPARAM>(path))) { Stop(); return; }
	TVTest::ChannelInfo channel{};
	channel.Size = sizeof(channel);
	if (!app_->GetCurrentChannelInfo(&channel)) { Hide(); return; }
	// チャンネル未スキャンのファイル再生におけるサービスIDの補完
	TVTest::ServiceInfo serviceInfo{};
	int serviceIndex = app_->GetService();
	if (serviceIndex >= 0 && app_->GetServiceInfo(serviceIndex, &serviceInfo)) channel.ServiceID = serviceInfo.ServiceID;
	if (!channel.ServiceID) { Hide(); return; }
	if (path_ != path || network_ != channel.NetworkID || transport_ != channel.TransportStreamID || service_ != channel.ServiceID) {
		Stop();
		path_ = path;
		network_ = channel.NetworkID;
		transport_ = channel.TransportStreamID;
		service_ = channel.ServiceID;
		recordingId_ = std::to_string(++serial_);
		openedAt_ = GetTickCount();
	}
	position_ = static_cast<int>(SendMessage(frame_, WM_TVTP_GET_POSITION, 0, 0));
	duration_ = static_cast<int>(SendMessage(frame_, WM_TVTP_GET_DURATION, 0, 0));
	if (duration_ <= 0 || position_ < 0) { Hide(); return; }
	if (!start_ && GetTickCount() - openedAt_ >= 2000) {
		// 公開メッセージ第2版の絶対時刻と、第1版のTOT日付補完
		if (SendMessage(frame_, WM_TVTP_GET_MSGVER, 0, 0) >= 2)
			start_ = static_cast<DWORD>(SendMessage(frame_, WM_TVTP_GET_TOT_UNIX, 0, 0)) * 1000LL;
		if (!start_) start_ = ResolveStartTime(static_cast<int>(SendMessage(frame_, WM_TVTP_GET_TOT_TIME, 0, 0)), clock_(), position_);
	}
	SendRequest();
	HWND status = FindWindowEx(frame_, nullptr, L"TvtPlay Status", nullptr);
	// TVTestのステータスバーへの統合版TvtPlayへの対応
	if (!status) {
		if (HWND host = HostWindow()) EnumChildWindows(host, FindHostStatus, reinterpret_cast<LPARAM>(&status));
	}
	AttachStatus(status);
	UpdatePlacement(true);
}

void TvtPlayCommentGraph::AttachStatus(HWND status)
{
	HWND root = status ? GetAncestor(status, GA_ROOT) : nullptr;
	HWND layout = root == frame_ ? root : nullptr;
	if (statusWindow_ == status && layoutWindow_ == layout) return;
	if (statusWindow_) RemoveWindowSubclass(statusWindow_, LayoutSubclass, reinterpret_cast<UINT_PTR>(this));
	if (layoutWindow_) RemoveWindowSubclass(layoutWindow_, LayoutSubclass, reinterpret_cast<UINT_PTR>(this));
	if (statusWindow_ != status) {
		cachedBar_ = {};
		cachedSize_ = {};
	}
	statusWindow_ = status;
	layoutWindow_ = layout;
	if (statusWindow_) SetWindowSubclass(statusWindow_, LayoutSubclass, reinterpret_cast<UINT_PTR>(this), reinterpret_cast<DWORD_PTR>(this));
	if (layoutWindow_ && layoutWindow_ != statusWindow_)
		SetWindowSubclass(layoutWindow_, LayoutSubclass, reinterpret_cast<UINT_PTR>(this), reinterpret_cast<DWORD_PTR>(this));
}

HWND TvtPlayCommentGraph::HostWindow() const
{
	return app_->GetFullscreen() ? FindProcessWindow(L"TVTest Fullscreen", true) : app_->GetAppWindow();
}

void TvtPlayCommentGraph::UpdatePlacement(bool redraw)
{
	if (placing_) return;
	placing_ = true;
	struct Reset { bool &flag; ~Reset() { flag = false; } } reset{ placing_ };
	if (!pluginEnabled_ || !enabled_ || duration_ <= 0 || !IsWindowVisible(statusWindow_) || IsIconic(app_->GetAppWindow())) {
		Hide();
		return;
	}
	HWND parent = HostWindow();
	HWND root = GetAncestor(statusWindow_, GA_ROOT);
	if (!parent || (root != parent && root != frame_)) { Hide(); return; }
	AttachStatus(statusWindow_);
	RECT rect;
	if (!LocateBar(statusWindow_, &rect)) { Hide(); return; }
	int dpi = app_->GetDPIFromWindow(statusWindow_);
	if (dpi <= 0) dpi = 96;
	// 録画を開いてからの初期表示と、シークバー・チャプター領域のホバー表示
	if (GetTickCount() - openedAt_ >= INITIAL_DISPLAY_MS) {
		POINT cursor{};
		RECT hover = rect;
		InflateRect(&hover, 2, MulDiv(8, dpi, 96));
		if (!GetCursorPos(&cursor) || WindowFromPoint(cursor) != statusWindow_
			|| !ScreenToClient(statusWindow_, &cursor) || !PtInRect(&hover, cursor)) {
			Hide();
			return;
		}
	}
	// TVTestのクライアント領域内でのシークバーへの固定配置
	POINT origin{ rect.left, 0 };
	MapWindowPoints(statusWindow_, parent, &origin, 1);
	RECT client;
	if (!GetClientRect(parent, &client)) { Hide(); return; }
	int height = MulDiv(height_, dpi, 96);
	int bottom = min(origin.y, client.bottom);
	RECT target{ origin.x, bottom - height, origin.x + rect.right - rect.left, bottom };
	if (target.top < client.top) {
		RECT statusRect;
		GetWindowRect(statusWindow_, &statusRect);
		MapWindowPoints(nullptr, parent, reinterpret_cast<POINT *>(&statusRect), 2);
		target.top = max(client.top, statusRect.bottom);
		target.bottom = target.top + height;
	}
	if (target.bottom > client.bottom) { Hide(); return; }
	if (overlay_ && !IsWindow(overlay_)) overlay_ = nullptr;
	bool changed = !overlay_ || !EqualRect(&bounds_, &target) || GetParent(overlay_) != parent;
	if (!overlay_) {
		overlay_ = CreateWindowEx(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE,
			OVERLAY_CLASS, L"TvtPlayの盛り上がり", WS_CHILD, target.left, target.top, target.right - target.left, height,
			parent, nullptr, instance_, this);
	}
	if (!overlay_) return;
	// 全画面切替時の子コントロールの付け替え
	if (GetParent(overlay_) != parent) SetParent(overlay_, parent);
	bounds_ = target;
	if (!start_) { state_ = "loading"; information_ = L"録画の放送時刻待ち…"; }
	if (changed || !IsWindowVisible(overlay_)) {
		SetWindowPos(overlay_, HWND_TOP, target.left, target.top, target.right - target.left, height, SWP_NOACTIVATE);
		Render();
		ShowWindow(overlay_, SW_SHOWNOACTIVATE);
	}
	else if (redraw) Render();
	UpdateCommentClip();
}

LRESULT CALLBACK TvtPlayCommentGraph::LayoutSubclass(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR id, DWORD_PTR data)
{
	LRESULT result = DefSubclassProc(hwnd, message, wParam, lParam);
	auto self = reinterpret_cast<TvtPlayCommentGraph *>(data);
	if (hwnd == self->statusWindow_ && message == WM_MOUSEMOVE) {
		TRACKMOUSEEVENT track{ sizeof(track), TME_LEAVE, hwnd, 0 };
		TrackMouseEvent(&track);
	}
	if (message == WM_NCDESTROY) {
		RemoveWindowSubclass(hwnd, LayoutSubclass, id);
		if (self->statusWindow_ == hwnd) self->statusWindow_ = nullptr;
		if (self->layoutWindow_ == hwnd) self->layoutWindow_ = nullptr;
		self->Hide();
	}
	// バーの描画・配置・表示・マウス移動に同期した更新
	else if ((hwnd == self->statusWindow_ && (message == WM_PAINT || message == WM_WINDOWPOSCHANGED
		|| message == WM_SHOWWINDOW || message == WM_MOUSEMOVE || message == WM_MOUSELEAVE))
		|| (hwnd == self->layoutWindow_ && (message == WM_SIZE || message == WM_SHOWWINDOW
			|| (hwnd == self->frame_ && message == WM_WINDOWPOSCHANGED)))) {
		self->UpdatePlacement();
	}
	return result;
}

// シークバーの固定高さの外枠からの描画範囲の検出
bool TvtPlayCommentGraph::FindSeekBar(const DWORD *pixels, int width, int height, RECT *bar)
{
	if (!pixels || width < 64 || height < 10 || !bar) return false;
	int bestWidth = 0;
	RECT best{};
	for (int y = 1; y + 8 < height; ++y) {
		int first = -1, last = -1;
		DWORD lineColor = 0;
		for (int x = 1; x < width - 1;) {
			DWORD color = pixels[y * width + x] & 0xffffff;
			int end = x + 1;
			while (end < width && (pixels[y * width + end] & 0xffffff) == color) ++end;
			// チャプター記号が接する上辺と、外枠の左右の境界判定
			bool outline = end - x >= 24 && end < width && (pixels[(y + 3) * width + x - 1] & 0xffffff) != color
				&& (pixels[(y + 3) * width + end] & 0xffffff) != color
				&& std::any_of(pixels + (y - 1) * width + x, pixels + (y - 1) * width + end,
				[color](DWORD pixel) { return (pixel & 0xffffff) != color; });
			if (outline) {
				for (int px = x; px < end && outline; ++px)
					outline = (pixels[(y + 7) * width + px] & 0xffffff) == color;
				for (int py = y + 1; py < y + 7 && outline; ++py)
					outline = (pixels[py * width + x] & 0xffffff) == color && (pixels[py * width + end - 1] & 0xffffff) == color;
			}
			if (outline) {
				if (first < 0 || color != lineColor || x - last > 512) first = x;
				last = end;
				lineColor = color;
				if (last - first > bestWidth) {
					bestWidth = last - first;
					best = { first + 2, y + 2, last - 2, y + 6 };
				}
			}
			x = end;
		}
	}
	if (bestWidth < 60) return false;
	*bar = best;
	return true;
}

bool TvtPlayCommentGraph::LocateBar(HWND status, RECT *rect)
{
	RECT client;
	if (!GetClientRect(status, &client)) return false;
	int width = client.right, height = client.bottom;
	if (statusWindow_ != status || cachedSize_.cx != width || cachedSize_.cy != height) {
		cachedBar_ = {};
		cachedSize_ = { width, height };
		statusWindow_ = status;
	}
	POINT cursor;
	GetCursorPos(&cursor);
	ScreenToClient(status, &cursor);
	// ホバー時の時刻表示による外枠の分割中の確定済み矩形の保持
	if (cachedBar_.right > cachedBar_.left && PtInRect(&client, cursor)) { *rect = cachedBar_; return true; }
	if (width <= 0 || width > 16384 || height <= 0 || height > 512) return false;
	HDC dc = GetDC(status), memory = CreateCompatibleDC(dc);
	BITMAPINFO bitmap{};
	bitmap.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
	bitmap.bmiHeader.biWidth = width;
	bitmap.bmiHeader.biHeight = -height;
	bitmap.bmiHeader.biPlanes = 1;
	bitmap.bmiHeader.biBitCount = 32;
	void *pixels = nullptr;
	HBITMAP image = CreateDIBSection(dc, &bitmap, DIB_RGB_COLORS, &pixels, nullptr, 0);
	bool found = false;
	if (dc && memory && image) {
		HGDIOBJ previous = SelectObject(memory, image);
		if (BitBlt(memory, 0, 0, width, height, dc, 0, 0, SRCCOPY)) {
			GdiFlush();
			found = FindSeekBar(static_cast<DWORD *>(pixels), width, height, rect);
		}
		SelectObject(memory, previous);
	}
	if (image) DeleteObject(image);
	if (memory) DeleteDC(memory);
	if (dc) ReleaseDC(status, dc);
	if (found) cachedBar_ = *rect;
	else cachedBar_ = {};
	return found;
}

void TvtPlayCommentGraph::Render()
{
	int width = bounds_.right - bounds_.left, height = bounds_.bottom - bounds_.top;
	if (!overlay_ || width <= 0 || height <= 0 || duration_ <= 0) return;
	HDC screen = GetDC(nullptr), dc = CreateCompatibleDC(screen);
	HBITMAP image = CreateCompatibleBitmap(screen, width, height);
	if (!dc || !image) {
		if (image) DeleteObject(image);
		if (dc) DeleteDC(dc);
		if (screen) ReleaseDC(nullptr, screen);
		return;
	}
	HGDIOBJ previous = SelectObject(dc, image);
	RECT area{ 0, 0, width, height };
	HBRUSH brush = static_cast<HBRUSH>(GetStockObject(DC_BRUSH));
	SetDCBrushColor(dc, colors_.background);
	FillRect(dc, &area, brush);
	int dpi = app_->GetDPIFromWindow(statusWindow_);
	if (dpi <= 0) dpi = 96;
	int pad = max(1, MulDiv(4, dpi, 96)), unit = max(1, MulDiv(1, dpi, 96));
	LOGFONT font{};
	if (!app_->GetFont(L"StatusBarFont", &font, dpi) || !font.lfHeight) {
		GetObject(GetStockObject(DEFAULT_GUI_FONT), sizeof(font), &font);
		font.lfHeight = -MulDiv(13, dpi, 96);
	}
	if (abs(font.lfHeight) > height / 3) { font.lfHeight = -max(1, height / 3); font.lfWidth = 0; }
	HFONT labelFont = CreateFontIndirect(&font);
	HGDIOBJ previousFont = SelectObject(dc, labelFont ? labelFont : GetStockObject(DEFAULT_GUI_FONT));
	SetBkMode(dc, TRANSPARENT);
	TEXTMETRIC metrics{};
	GetTextMetrics(dc, &metrics);
	int band = min(height / 2, static_cast<int>(metrics.tmHeight) + pad);
	bool compact = height - band * 2 < pad * 3;
	int maximum = counts_.empty() ? -1 : *std::max_element(counts_.begin(), counts_.end());
	wchar_t title[96];
	if (maximum < 0) wcscpy_s(title, L"盛り上がり · コメント/10秒");
	else swprintf_s(title, L"盛り上がり  最大 %d 件/10秒", maximum);
	const UINT format = DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX | DT_END_ELLIPSIS;
	bool statusOnly = compact && state_ != "ready" && !information_.empty();
	SetTextColor(dc, statusOnly && state_ == "error" ? colors_.error : colors_.text);
	RECT label{ pad, 0, width - pad, band };
	DrawText(dc, statusOnly ? information_.c_str() : title, -1, &label, format);
	int top = band, bottom = height - (compact ? pad : band) - 1;
	HPEN grid = CreatePen(PS_SOLID, unit, colors_.grid);
	HGDIOBJ previousPen = SelectObject(dc, grid);
	MoveToEx(dc, 0, bottom, nullptr);
	LineTo(dc, width, bottom);
	SelectObject(dc, previousPen);
	DeleteObject(grid);
	HPEN line = CreatePen(PS_SOLID, max(1, MulDiv(2, dpi, 96)), colors_.line);
	previousPen = SelectObject(dc, line);
	bool connected = false;
	for (size_t i = 0; i < counts_.size(); ++i) {
		if (counts_[i] < 0) { connected = false; continue; }
		LONGLONG begin = static_cast<LONGLONG>(i) * ACTIVITY_INTERVAL_MS;
		LONGLONG end = min(begin + ACTIVITY_INTERVAL_MS, static_cast<LONGLONG>(requestedDuration_));
		int x = static_cast<int>((begin + end) / 2 * (width - 1) / duration_);
		int y = bottom - static_cast<int>(static_cast<LONGLONG>(counts_[i]) * max(1, bottom - top) / max(1, maximum));
		if (connected) LineTo(dc, x, y);
		else { MoveToEx(dc, x, y, nullptr); LineTo(dc, x + 1, y); }
		connected = true;
	}
	SelectObject(dc, previousPen);
	DeleteObject(line);
	// 折れ線と同系色でも識別できる現在位置の縁取りと三角マーカー
	int x = static_cast<int>(static_cast<LONGLONG>(max(0, min(position_, duration_))) * (width - 1) / duration_);
	RECT stem{ x - unit / 2, top, x - unit / 2 + unit, bottom + 1 }, outline = stem;
	InflateRect(&outline, unit, 0);
	SetDCBrushColor(dc, colors_.background);
	FillRect(dc, &outline, brush);
	SetDCBrushColor(dc, colors_.playhead);
	FillRect(dc, &stem, brush);
	int radius = min(pad, bottom - top);
	POINT cap[]{ { x - radius, top }, { x + radius + 1, top }, { x, top + radius + 1 } };
	HGDIOBJ previousBrush = SelectObject(dc, brush);
	previousPen = SelectObject(dc, GetStockObject(DC_PEN));
	SetDCPenColor(dc, colors_.background);
	Polygon(dc, cap, _countof(cap));
	SelectObject(dc, previousBrush);
	SelectObject(dc, previousPen);
	if (!compact) {
		SetTextColor(dc, state_ == "error" ? colors_.error : colors_.text);
		label = { pad, height - band, width - pad, height };
		DrawText(dc, information_.c_str(), -1, &label, format);
	}
	SelectObject(dc, previousFont);
	DeleteObject(labelFont);
	POINT origin{};
	SIZE size{ width, height };
	BLENDFUNCTION blend{ AC_SRC_OVER, 0, static_cast<BYTE>(opacity_), 0 };
	UpdateLayeredWindow(overlay_, screen, nullptr, &size, dc, &origin, 0, &blend, ULW_ALPHA);
	SelectObject(dc, previous);
	DeleteObject(image);
	DeleteDC(dc);
	ReleaseDC(nullptr, screen);
}

LRESULT CALLBACK TvtPlayCommentGraph::WindowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
{
	if (message == WM_NCCREATE)
		SetWindowLongPtr(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(reinterpret_cast<CREATESTRUCT *>(lParam)->lpCreateParams));
	auto self = reinterpret_cast<TvtPlayCommentGraph *>(GetWindowLongPtr(hwnd, GWLP_USERDATA));
	if (self) {
		if (message == WM_NCDESTROY) {
			if (self->overlay_ == hwnd) {
				self->overlay_ = nullptr;
				self->ClearCommentClip();
			}
			SetWindowLongPtr(hwnd, GWLP_USERDATA, 0);
		}
		if (message == WM_TIMER) { self->Tick(); return 0; }
		if (message == WM_ACTIVITY_MESSAGE) { self->Receive(); return 0; }
		if (message == WM_ACTIVITY_COLORS) { self->UpdateColors(); return 0; }
		if (message == WM_NCHITTEST) return HTTRANSPARENT;
		if (message == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
	}
	return DefWindowProc(hwnd, message, wParam, lParam);
}
