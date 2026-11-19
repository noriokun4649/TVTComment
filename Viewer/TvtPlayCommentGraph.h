#pragma once
#include "NicoJK/TVTestPlugin.h"
#include "TVTComment/IPC/IPCMessage/PanelIPCMessage.h"
#include <deque>
#include <functional>
#include <mutex>
#include <string>
#include <vector>

// TvtPlayの再生範囲に対応するコメント数グラフ
class TvtPlayCommentGraph
{
public:
	static const int CommandId = 3;
	using Sender = std::function<bool(const TVTComment::IIPCMessage &)>;
	using Clock = std::function<LONGLONG()>;
	bool Initialize(HINSTANCE instance, TVTest::CTVTestApp *app, Sender sender, Clock clock, const std::wstring &iniPath);
	void Finalize();
	void SetPluginEnabled(bool enabled);
	void OnColorChange();
	bool OnCommand(int command);
	void OnMessage(const TVTComment::IIPCMessage &message);

	static bool FindSeekBar(const DWORD *pixels, int width, int height, RECT *bar);
	static LONGLONG ResolveStartTime(int firstTime, LONGLONG currentUnixMilliseconds, int position);

private:
	static LRESULT CALLBACK WindowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam);
	static LRESULT CALLBACK LayoutSubclass(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR id, DWORD_PTR data);
	static LRESULT CALLBACK CommentSubclass(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR id, DWORD_PTR data);
	void SetEnabled(bool enabled);
	bool Send(const char *operation, std::vector<std::string> arguments = {});
	void Tick();
	void Receive();
	void UpdateColors();
	void Render();
	void Hide();
	void Stop();
	void AttachStatus(HWND status);
	HWND HostWindow() const;
	void UpdatePlacement(bool redraw = false);
	void UpdateCommentClip();
	void ClearCommentClip();
	bool LocateBar(HWND status, RECT *rect);
	void ResetCounts(int count);
	void SendRequest();
	HINSTANCE instance_ = nullptr;
	TVTest::CTVTestApp *app_ = nullptr;
	HWND dispatcher_ = nullptr, overlay_ = nullptr, frame_ = nullptr, statusWindow_ = nullptr, layoutWindow_ = nullptr;
	HWND commentWindow_ = nullptr;
	Sender sender_;
	Clock clock_;
	std::wstring iniPath_, path_, information_;
	std::string requestId_, recordingId_, state_;
	std::vector<int> counts_;
	struct {
		COLORREF background, text, grid, line, playhead, error;
	} colors_{};
	std::mutex mutex_;
	std::deque<TVTComment::PanelIPCMessage> pending_;
	bool pluginEnabled_ = false, enabled_ = true, ready_ = false, accepting_ = false;
	bool requestSent_ = false, placing_ = false, clipping_ = false;
	int height_ = 72, opacity_ = 220, position_ = 0, duration_ = 0, requestedDuration_ = 0;
	unsigned long long serial_ = 0, revision_ = 0;
	DWORD openedAt_ = 0;
	LONGLONG start_ = 0;
	WORD network_ = 0, transport_ = 0, service_ = 0;
	RECT cachedBar_ = {}, bounds_ = {};
	SIZE cachedSize_ = {};
	RECT commentClip_ = {};
	SIZE commentSize_ = {};
};
