#pragma once
#include "NicoJK/TVTestPlugin.h"
#include "TVTComment/IPC/IPCMessage/PanelIPCMessage.h"
#include "TVTCommentPanelTheme.h"
#include "TVTCommentPanelSourceList.h"
#include <functional>
#include <mutex>
#include <deque>

class TVTCommentPanel
{
public:
	using Sender = std::function<bool(const TVTComment::IIPCMessage &)>;
	bool Initialize(HINSTANCE instance, TVTest::CTVTestApp *app, Sender sender, TVTest::PluginParam *parameter = nullptr);
	void Finalize();
	bool OnPanelEvent(TVTest::PanelItemEventInfo *event);
	void SetPluginEnabled(bool enabled);
	void OnMessage(const TVTComment::IIPCMessage &message); // IPC受信スレッドから呼び出し可
	void OnColorChange();

private:
	struct Row
	{
		std::string Id;
		std::vector<std::wstring> Cells;
	};
	using Rows = std::vector<Row>;
	static constexpr int PANEL_ID = 1;
	static LRESULT CALLBACK WindowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam);
	static LRESULT CALLBACK DispatchWindowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam);
	static LRESULT CALLBACK ChatListSubclass(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR id, DWORD_PTR data);
	LRESULT HandleWindowMessage(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam);
	void CreateControls();
	void Layout();
	void UpdateStyle();
	void UpdateControls();
	bool ChatsAtBottom() const;
	void FollowChats();
	void Subscribe(bool active);
	void UpdateVisibility();
	void SendPostDraft(bool submit);
	std::wstring PostMode() const;
	void DispatchMessages();
	void ApplyMessage(const TVTComment::PanelIPCMessage &message);
	bool Send(const std::string &operation, std::vector<std::string> arguments = {});
	void SendSelection(HWND list, const Rows &rows, const char *operation);
	HWND CreateControl(LPCWSTR className, LPCWSTR text, DWORD style, int id);
	HWND CreateList(int id, std::initializer_list<std::pair<LPCWSTR, int>> columns);
	void SetRows(HWND list, Rows &rows, Rows replacement);
	void SetChoices(HWND combo, Rows &rows, Rows replacement, const std::string &selected);
	std::string SelectedId(HWND list, const Rows &rows) const;
	static std::string SelectedChoice(HWND combo, const Rows &rows);
	static Rows ReadRows(const std::vector<std::string> &fields, size_t &offset, size_t count, size_t columns);

	HINSTANCE instance_ = nullptr;
	TVTest::CTVTestApp *app_ = nullptr;
	Sender sender_;
	HWND dispatcher_ = nullptr, window_ = nullptr;
	HWND tabs_ = nullptr, status_ = nullptr, open_ = nullptr;
	HWND chats_ = nullptr, channels_ = nullptr;
	HWND available_ = nullptr, sources_ = nullptr, automatic_ = nullptr, add_ = nullptr, remove_ = nullptr;
	HWND postService_ = nullptr, postMail_ = nullptr, postText_ = nullptr, postThread_ = nullptr, postButton_ = nullptr;
	HFONT font_ = nullptr;
	TVTCommentPanelTheme theme_;
	TVTCommentPanelSourceList sourceList_;
	int dpi_ = 96;
	DWORD backendProcessId_ = 0;
	bool enabled_ = false, ready_ = false, active_ = false, subscribed_ = false, automaticSources_ = true, canAddSource_ = false;
	bool reportedVisible_ = false, updatingPost_ = false, postingReady_ = false, postPending_ = false;
	bool followChats_ = true, updatingChats_ = false;
	unsigned long long postRevision_ = 0, submittedRevision_ = 0;
	Rows chatRows_, channelRows_, availableRows_, sourceRows_;
	Rows postServiceRows_, postThreadRows_;
	std::mutex mutex_;
	std::deque<TVTComment::PanelIPCMessage> pending_;
};
