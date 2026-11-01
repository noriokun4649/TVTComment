#include "stdafx.h"
#include "TVTCommentPanel.h"
#include "TVTCommentPanelDropDown.h"
#include "resource.h"
#include <commctrl.h>
#include <codecvt>
#include <locale>
#include <stdexcept>

#pragma comment(lib, "comctl32.lib")

namespace
{
	const wchar_t PANEL_CLASS[] = L"TVTComment.Panel";
	const wchar_t DISPATCH_CLASS[] = L"TVTComment.Panel.Dispatch";
	const UINT WM_PANEL_MESSAGE = WM_APP + 1;
	const UINT WM_PANEL_THEME_CHANGE = WM_APP + 2;
	const UINT_PTR VISIBILITY_TIMER = 1;
	enum ControlId { Tabs = 100, Status, Open, Chats, Channels, Available, Sources, Automatic, Add, Remove,
		PostService, PostMail, PostText, PostThread, PostButton };
	enum Page { ChannelPage, ChatPage, SourcePage };

	std::wstring Wide(const std::string &text)
	{
		std::wstring_convert<std::codecvt_utf8_utf16<wchar_t>> converter;
		return converter.from_bytes(text);
	}

	std::wstring Text(HWND control)
	{
		std::wstring text(GetWindowTextLength(control) + 1, L'\0');
		text.resize(GetWindowText(control, &text[0], static_cast<int>(text.size())));
		return text;
	}

	void SetText(HWND control, LPCWSTR text)
	{
		if (Text(control) != text) SetWindowText(control, text);
	}

	void SetEnabled(HWND control, bool enabled)
	{
		if ((IsWindowEnabled(control) != FALSE) != enabled) EnableWindow(control, enabled);
	}

	std::string Utf8(const std::wstring &text)
	{
		std::wstring_convert<std::codecvt_utf8_utf16<wchar_t>> converter;
		return converter.to_bytes(text);
	}

	size_t Count(const std::string &text)
	{
		if (text.empty() || text.find_first_not_of("0123456789") != std::string::npos)
			throw std::invalid_argument("Invalid panel count");
		return static_cast<size_t>(std::stoul(text));
	}

	LRESULT CALLBACK ControlSubclass(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR id, DWORD_PTR)
	{
		if (GetDlgCtrlID(hwnd) == PostText) {
			if (message == WM_IME_STARTCOMPOSITION) SetProp(hwnd, L"Panel.Composing", reinterpret_cast<HANDLE>(1));
			if (message == WM_IME_ENDCOMPOSITION) RemoveProp(hwnd, L"Panel.Composing");
			if ((message == WM_KEYDOWN || message == WM_SYSKEYDOWN) && wParam == VK_RETURN && !GetProp(hwnd, L"Panel.Composing")) {
				if (GetKeyState(VK_MENU) < 0) SendMessage(hwnd, EM_REPLACESEL, TRUE, reinterpret_cast<LPARAM>(L"\r\n"));
				else if (!(lParam & (1 << 30))) SendMessage(GetParent(hwnd), WM_COMMAND, MAKEWPARAM(PostButton, BN_CLICKED), 0);
				return 0;
			}
			if ((message == WM_CHAR || message == WM_SYSCHAR) && wParam == VK_RETURN) return 0;
		}
		if (message == WM_KEYDOWN && wParam == VK_TAB) {
			HWND next = GetNextDlgTabItem(GetParent(hwnd), hwnd, GetKeyState(VK_SHIFT) < 0);
			if (next) SetFocus(next);
			return 0;
		}
		if (message == WM_CHAR && wParam == VK_TAB) return 0;
		if (message == WM_NCDESTROY) {
			RemoveProp(hwnd, L"Panel.Composing");
			RemoveWindowSubclass(hwnd, ControlSubclass, id);
		}
		return DefSubclassProc(hwnd, message, wParam, lParam);
	}
}

bool TVTCommentPanel::Initialize(HINSTANCE instance, TVTest::CTVTestApp *app, Sender sender, TVTest::PluginParam *parameter)
{
	instance_ = instance;
	app_ = app;
	theme_.Initialize(app, parameter);
	sender_ = std::move(sender);
	INITCOMMONCONTROLSEX controls{ sizeof(controls), ICC_LISTVIEW_CLASSES | ICC_TAB_CLASSES };
	InitCommonControlsEx(&controls);
	WNDCLASSEX wc{};
	wc.cbSize = sizeof(wc);
	wc.hInstance = instance_;
	wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
	wc.hbrBackground = nullptr;
	wc.lpfnWndProc = WindowProc;
	wc.lpszClassName = PANEL_CLASS;
	if (!RegisterClassEx(&wc)) return false;
	wc.lpfnWndProc = DispatchWindowProc;
	wc.lpszClassName = DISPATCH_CLASS;
	if (!RegisterClassEx(&wc)) { Finalize(); return false; }
	dispatcher_ = CreateWindowEx(0, DISPATCH_CLASS, L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, instance_, this);
	if (!dispatcher_) { Finalize(); return false; }

	TVTest::PanelItemInfo info{};
	info.Size = sizeof(info);
	info.Style = TVTest::PANEL_ITEM_STYLE_NEEDFOCUS;
	info.ID = PANEL_ID;
	info.pszIDText = L"TVTCommentPanel";
	info.pszTitle = L"TvtComment";
	info.hbmIcon = static_cast<HBITMAP>(LoadImage(instance_, MAKEINTRESOURCE(IDB_TVTCICON), IMAGE_BITMAP, 0, 0, LR_CREATEDIBSECTION));
	bool registered = app_->RegisterPanelItem(&info);
	DeleteObject(info.hbmIcon);
	if (!registered) Finalize();
	return registered;
}

void TVTCommentPanel::Finalize()
{
	if (window_) DestroyWindow(window_);
	sourceList_.Finalize();
	if (dispatcher_) DestroyWindow(dispatcher_);
	dispatcher_ = nullptr;
	if (font_) DeleteObject(font_);
	font_ = nullptr;
	if (instance_) {
		UnregisterClass(PANEL_CLASS, instance_);
		UnregisterClass(DISPATCH_CLASS, instance_);
	}
	sender_ = {};
}

bool TVTCommentPanel::OnPanelEvent(TVTest::PanelItemEventInfo *event)
{
	if (!event || event->ID != PANEL_ID || !dispatcher_) return false;
	switch (event->Event) {
	case TVTest::PANEL_ITEM_EVENT_CREATE:
	{
		auto create = reinterpret_cast<TVTest::PanelItemCreateEventInfo *>(event);
		const RECT &rect = create->ItemRect;
		// TVTestは親パネルだけを表示する
		window_ = CreateWindowEx(WS_EX_CONTROLPARENT, PANEL_CLASS, L"TvtComment", WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
			rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, create->hwndParent, nullptr, instance_, this);
		create->hwndItem = window_;
		return window_ != nullptr;
	}
	case TVTest::PANEL_ITEM_EVENT_ACTIVATE:
		active_ = true;
		Subscribe(true);
		UpdateVisibility();
		return true;
	case TVTest::PANEL_ITEM_EVENT_DEACTIVATE:
	case TVTest::PANEL_ITEM_EVENT_DISABLE:
		active_ = false;
		UpdateVisibility();
		Subscribe(false);
		return true;
	case TVTest::PANEL_ITEM_EVENT_STYLECHANGED:
	case TVTest::PANEL_ITEM_EVENT_FONTCHANGED:
		UpdateStyle();
		Layout();
		return true;
	}
	return false;
}

void TVTCommentPanel::SetPluginEnabled(bool enabled)
{
	enabled_ = enabled;
	ready_ = subscribed_ = false;
	reportedVisible_ = postingReady_ = postPending_ = false;
	postRevision_ = submittedRevision_ = 0;
	backendProcessId_ = 0;
	{
		std::lock_guard<std::mutex> lock(mutex_);
		pending_.clear();
	}
	SetRows(chats_, chatRows_, {});
	followChats_ = true;
	SetRows(channels_, channelRows_, {});
	SetRows(available_, availableRows_, {});
	SetRows(sources_, sourceRows_, {});
	updatingPost_ = true;
	SetChoices(postService_, postServiceRows_, {}, "");
	SetChoices(postThread_, postThreadRows_, {}, "");
	SetWindowText(postText_, L"");
	SetWindowText(postMail_, L"");
	updatingPost_ = false;
	UpdateControls();
}

void TVTCommentPanel::OnMessage(const TVTComment::IIPCMessage &message)
{
	auto panel = dynamic_cast<const TVTComment::PanelIPCMessage *>(&message);
	if (!panel || !dispatcher_) return;
	{
		std::lock_guard<std::mutex> lock(mutex_);
		pending_.push_back(*panel);
	}
	// UIの更新はウィンドウスレッドで行う
	PostMessage(dispatcher_, WM_PANEL_MESSAGE, 0, 0);
}

LRESULT CALLBACK TVTCommentPanel::DispatchWindowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
{
	if (message == WM_NCCREATE)
		SetWindowLongPtr(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(reinterpret_cast<CREATESTRUCT *>(lParam)->lpCreateParams));
	auto self = reinterpret_cast<TVTCommentPanel *>(GetWindowLongPtr(hwnd, GWLP_USERDATA));
	if (self && message == WM_PANEL_MESSAGE) { self->DispatchMessages(); return 0; }
	return DefWindowProc(hwnd, message, wParam, lParam);
}

void TVTCommentPanel::DispatchMessages()
{
	std::deque<TVTComment::PanelIPCMessage> messages;
	{
		std::lock_guard<std::mutex> lock(mutex_);
		messages.swap(pending_);
	}
	if (!enabled_) return;
	for (const auto &message : messages) {
		try { ApplyMessage(message); }
		catch (const std::exception &) {
			SetWindowText(status_, L"パネルの受信データが不正です");
			// 再購読して全件取得する
			Send("Subscribe");
		}
	}
}

bool TVTCommentPanel::Send(const std::string &operation, std::vector<std::string> arguments)
{
	if (!enabled_ || !ready_ || !sender_) return false;
	// 設定ウィンドウを前面に出せるようにする
	if (backendProcessId_ && (operation == "ShowWindow" || operation == "AddSource" || operation == "PostChat"))
		AllowSetForegroundWindow(backendProcessId_);
	TVTComment::PanelIPCMessage message;
	message.Operation = operation;
	message.Arguments = std::move(arguments);
	return sender_(message);
}

void TVTCommentPanel::Subscribe(bool active)
{
	if (!ready_ || active == subscribed_) return;
	if (Send(active ? "Subscribe" : "Unsubscribe")) subscribed_ = active;
}

void TVTCommentPanel::UpdateVisibility()
{
	// 親パネルの折り畳みはWM_SHOWWINDOWでは検出できない
	bool visible = active_ && window_ && IsWindowVisible(window_) && !IsIconic(GetAncestor(window_, GA_ROOT));
	if (visible != reportedVisible_ && Send("Visibility", { visible ? "1" : "0" })) reportedVisible_ = visible;
}

TVTCommentPanel::Rows TVTCommentPanel::ReadRows(const std::vector<std::string> &fields, size_t &offset, size_t count, size_t columns)
{
	if (offset > fields.size() || count > (fields.size() - offset) / (columns + 1))
		throw std::invalid_argument("Incomplete panel rows");
	Rows result;
	result.reserve(count);
	for (size_t i = 0; i < count; ++i) {
		Row row;
		row.Id = fields[offset++];
		for (size_t j = 0; j < columns; ++j) row.Cells.push_back(Wide(fields[offset++]));
		result.push_back(std::move(row));
	}
	return result;
}

void TVTCommentPanel::ApplyMessage(const TVTComment::PanelIPCMessage &message)
{
	const auto &fields = message.Arguments;
	if (message.Operation == "Ready") {
		if (fields.size() != 1) throw std::invalid_argument("Missing backend process ID");
		backendProcessId_ = static_cast<DWORD>(Count(fields[0]));
		ready_ = true;
		subscribed_ = false;
		reportedVisible_ = false;
		UpdateControls();
		// Readyより先にパネルが選択されていた場合も購読する
		Subscribe(active_);
		UpdateVisibility();
		return;
	}
	if (!subscribed_) return;
	size_t offset = 0;
	if (message.Operation == "Channels") {
		if (fields.size() % 4 != 0) throw std::invalid_argument("Invalid channels");
		auto rows = ReadRows(fields, offset, fields.size() / 4, 3);
		for (auto &row : rows) row.Cells[0] = row.Cells[0] == L"1" ? L"●" : L"";
		SetRows(channels_, channelRows_, std::move(rows));
	} else if (message.Operation == "Sources") {
		if (fields.size() < 4 || (fields[0] != "0" && fields[0] != "1") || (fields[1] != "0" && fields[1] != "1"))
			throw std::invalid_argument("Invalid sources");
		offset = 3;
		auto available = ReadRows(fields, offset, Count(fields[2]), 2);
		if (offset >= fields.size()) throw std::invalid_argument("Missing source count");
		size_t count = Count(fields[offset++]);
		auto sources = ReadRows(fields, offset, count, 2);
		if (offset != fields.size()) throw std::invalid_argument("Unexpected source fields");
		automaticSources_ = fields[0] == "1";
		canAddSource_ = fields[1] == "1";
		SetRows(available_, availableRows_, std::move(available));
		SetRows(sources_, sourceRows_, std::move(sources));
	} else if (message.Operation == "Posting") {
		if (fields.size() < 7) throw std::invalid_argument("Invalid posting state");
		if (fields[0].empty() || fields[0].find_first_not_of("0123456789") != std::string::npos)
			throw std::invalid_argument("Invalid draft revision");
		auto revision = std::stoull(fields[0]);
		offset = 5;
		auto services = ReadRows(fields, offset, Count(fields[4]), 2);
		if (fields.size() - offset < 2) throw std::invalid_argument("Missing threads");
		auto selectedThread = fields[offset++];
		size_t count = Count(fields[offset++]);
		auto threads = ReadRows(fields, offset, count, 1);
		if (offset != fields.size()) throw std::invalid_argument("Unexpected posting fields");
		// 古い通知で入力中の内容を上書きしない
		bool current = revision >= postRevision_;
		auto selectedService = current ? fields[1] : SelectedChoice(postService_, postServiceRows_);
		if (!current) selectedThread = SelectedChoice(postThread_, postThreadRows_);
		auto oldMode = PostMode(), oldDraft = Text(postText_);
		updatingPost_ = true;
		SetChoices(postService_, postServiceRows_, std::move(services), selectedService);
		SetChoices(postThread_, postThreadRows_, std::move(threads), selectedThread);
		if (current) {
			postRevision_ = revision;
			if (Text(postText_) != Wide(fields[2])) SetWindowText(postText_, Wide(fields[2]).c_str());
			if (Text(postMail_) != Wide(fields[3])) SetWindowText(postMail_, Wide(fields[3]).c_str());
		}
		updatingPost_ = false;
		postingReady_ = true;
		if (revision >= submittedRevision_) postPending_ = false;
		if (oldMode != PostMode() || oldDraft != Text(postText_)) Layout();
	} else if (message.Operation == "Chats") {
		if (fields.size() < 2 || (fields.size() - 2) % 6 != 0 || (fields[0] != "0" && fields[0] != "1"))
			throw std::invalid_argument("Invalid chats");
		offset = 2;
		auto additions = ReadRows(fields, offset, (fields.size() - 2) / 6, 5);
		size_t removed = Count(fields[1]);
		bool reset = fields[0] == "1";
		if (!reset && removed > chatRows_.size()) throw std::invalid_argument("Invalid chat removal");
		Rows rows;
		if (!reset) rows.assign(chatRows_.begin() + removed, chatRows_.end());
		rows.insert(rows.end(), std::make_move_iterator(additions.begin()), std::make_move_iterator(additions.end()));
		SetRows(chats_, chatRows_, std::move(rows));
		if (chatRows_.empty()) followChats_ = true;
		FollowChats();
	}
	UpdateControls();
}

std::string TVTCommentPanel::SelectedId(HWND list, const Rows &rows) const
{
	if (!list) return {};
	if (list == sources_) return sourceList_.SelectedId();
	int selected = ListView_GetNextItem(list, -1, LVNI_SELECTED);
	return selected >= 0 && static_cast<size_t>(selected) < rows.size() ? rows[selected].Id : std::string();
}

void TVTCommentPanel::SetRows(HWND list, Rows &rows, Rows replacement)
{
	if (&rows == &sourceRows_) {
		std::vector<TVTCommentPanelSourceList::Item> items;
		for (const auto &row : replacement) items.push_back({ row.Id, row.Cells[0], row.Cells[1] });
		rows = std::move(replacement);
		sourceList_.SetItems(std::move(items));
		return;
	}
	if (list == chats_) updatingChats_ = true;
	std::string selected = SelectedId(list, rows);
	int top = list ? ListView_GetTopIndex(list) : -1;
	std::string topId = top >= 0 && static_cast<size_t>(top) < rows.size() ? rows[top].Id : std::string();
	RECT oldTop{};
	if (list && top >= 0) ListView_GetItemRect(list, top, &oldTop, LVIR_BOUNDS);
	// WM_SETREDRAW(TRUE)で非表示のリストが表示されるのを防ぐ
	bool redraw = list && (GetWindowLongPtr(list, GWL_STYLE) & WS_VISIBLE) != 0;
	if (redraw) SendMessage(list, WM_SETREDRAW, FALSE, 0);
	rows = std::move(replacement);
	if (!list) { updatingChats_ = false; return; }
	ListView_SetItemCountEx(list, static_cast<int>(rows.size()), LVSICF_NOSCROLL);
	ListView_SetItemState(list, -1, 0, LVIS_SELECTED | LVIS_FOCUSED);
	for (size_t i = 0; i < rows.size(); ++i) {
		if (!selected.empty() && rows[i].Id == selected)
			ListView_SetItemState(list, static_cast<int>(i), LVIS_SELECTED | LVIS_FOCUSED, LVIS_SELECTED | LVIS_FOCUSED);
		if (!topId.empty() && rows[i].Id == topId) {
			ListView_EnsureVisible(list, static_cast<int>(i), FALSE);
			RECT newTop{};
			if (ListView_GetItemRect(list, static_cast<int>(i), &newTop, LVIR_BOUNDS))
				ListView_Scroll(list, 0, newTop.top - oldTop.top);
		}
	}
	if (redraw) SendMessage(list, WM_SETREDRAW, TRUE, 0);
	InvalidateRect(list, nullptr, FALSE);
	if (list == chats_) updatingChats_ = false;
}

bool TVTCommentPanel::ChatsAtBottom() const
{
	SCROLLINFO scroll{ sizeof(scroll), SIF_RANGE | SIF_PAGE | SIF_POS };
	if (!chats_ || chatRows_.empty() || !GetScrollInfo(chats_, SB_VERT, &scroll)) return true;
	return scroll.nPos >= scroll.nMax - static_cast<int>(max(1U, scroll.nPage)) + 1;
}

void TVTCommentPanel::FollowChats()
{
	if (!followChats_ || !chats_ || chatRows_.empty()) return;
	updatingChats_ = true;
	ListView_EnsureVisible(chats_, static_cast<int>(chatRows_.size()) - 1, FALSE);
	updatingChats_ = false;
}

LRESULT CALLBACK TVTCommentPanel::ChatListSubclass(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR id, DWORD_PTR data)
{
	auto self = reinterpret_cast<TVTCommentPanel *>(data);
	LRESULT result = DefSubclassProc(hwnd, message, wParam, lParam);
	if (!self->updatingChats_ && (message == WM_VSCROLL || message == WM_MOUSEWHEEL || message == WM_KEYDOWN))
		self->followChats_ = self->ChatsAtBottom();
	if (message == WM_NCDESTROY) RemoveWindowSubclass(hwnd, ChatListSubclass, id);
	return result;
}

void TVTCommentPanel::SendSelection(HWND list, const Rows &rows, const char *operation)
{
	auto id = SelectedId(list, rows);
	if (!id.empty()) Send(operation, { id });
}

std::string TVTCommentPanel::SelectedChoice(HWND combo, const Rows &rows)
{
	int selected = combo ? ComboBox_GetCurSel(combo) : CB_ERR;
	return selected >= 0 && static_cast<size_t>(selected) < rows.size() ? rows[selected].Id : std::string();
}

void TVTCommentPanel::SetChoices(HWND combo, Rows &rows, Rows replacement, const std::string &selected)
{
	bool changed = rows.size() != replacement.size();
	for (size_t i = 0; !changed && i < rows.size(); ++i)
		changed = rows[i].Id != replacement[i].Id || rows[i].Cells != replacement[i].Cells;
	rows = std::move(replacement);
	if (!combo) return;
	if (changed) {
		ComboBox_ResetContent(combo);
		for (const auto &row : rows) ComboBox_AddString(combo, row.Cells[0].c_str());
	}
	int index = -1;
	for (size_t i = 0; i < rows.size(); ++i) if (rows[i].Id == selected) index = static_cast<int>(i);
	if (ComboBox_GetCurSel(combo) != index) ComboBox_SetCurSel(combo, index);
}

std::wstring TVTCommentPanel::PostMode() const
{
	int selected = ComboBox_GetCurSel(postService_);
	return selected >= 0 && static_cast<size_t>(selected) < postServiceRows_.size() ? postServiceRows_[selected].Cells[1] : L"none";
}

void TVTCommentPanel::SendPostDraft(bool submit)
{
	if (updatingPost_ || !postingReady_ || !ready_ || (submit && (postPending_ || !IsWindowEnabled(postButton_)))) return;
	try {
		auto text = Utf8(Text(postText_)), mail = Utf8(Text(postMail_));
		auto revision = postRevision_ + 1;
		if (Send(submit ? "PostChat" : "PostDraft", { std::to_string(revision), SelectedChoice(postService_, postServiceRows_),
			SelectedChoice(postThread_, postThreadRows_), text, mail })) {
			postRevision_ = revision;
			if (submit) { submittedRevision_ = revision; postPending_ = true; }
		}
	} catch (const std::range_error &) {
		// サロゲートペアの入力が揃うまで待つ
	}
	UpdateControls();
}

HWND TVTCommentPanel::CreateControl(LPCWSTR className, LPCWSTR text, DWORD style, int id)
{
	HWND control = CreateWindowEx(0, className, text, WS_CHILD | style, 0, 0, 0, 0, window_, reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)), instance_, nullptr);
	if (style & WS_TABSTOP) SetWindowSubclass(control, ControlSubclass, 1, 0);
	return control;
}

HWND TVTCommentPanel::CreateList(int id, std::initializer_list<std::pair<LPCWSTR, int>> columns)
{
	HWND list = CreateControl(WC_LISTVIEW, L"", WS_TABSTOP | WS_BORDER | LVS_REPORT | LVS_OWNERDATA | LVS_SINGLESEL | LVS_SHOWSELALWAYS, id);
	ListView_SetExtendedListViewStyle(list, LVS_EX_FULLROWSELECT | LVS_EX_DOUBLEBUFFER | LVS_EX_INFOTIP);
	int index = 0;
	for (const auto &column : columns) {
		LVCOLUMN info{};
		info.mask = LVCF_TEXT | LVCF_WIDTH;
		info.pszText = const_cast<LPWSTR>(column.first);
		info.cx = column.second;
		ListView_InsertColumn(list, index++, &info);
	}
	return list;
}

void TVTCommentPanel::CreateControls()
{
	tabs_ = CreateControl(WC_TABCONTROL, L"", WS_VISIBLE | WS_TABSTOP | WS_CLIPSIBLINGS | TCS_FIXEDWIDTH, Tabs);
	const wchar_t *titles[] = { L"チャンネル", L"ログ", L"コメント元" };
	for (int i = 0; i < 3; ++i) {
		TCITEM item{};
		item.mask = TCIF_TEXT;
		item.pszText = const_cast<LPWSTR>(titles[i]);
		TabCtrl_InsertItem(tabs_, i, &item);
	}
	chats_ = CreateList(Chats, { { L"時刻", 65 }, { L"コメント", 260 }, { L"ソース", 120 }, { L"ユーザー", 130 }, { L"コメ番", 60 } });
	SetWindowSubclass(chats_, ChatListSubclass, 2, reinterpret_cast<DWORD_PTR>(this));
	channels_ = CreateList(Channels, { { L"選局中", 55 }, { L"チャンネル", 180 }, { L"勢い", 65 } });
	automatic_ = CreateControl(L"BUTTON", L"自動選択する", WS_TABSTOP | BS_AUTOCHECKBOX, Automatic);
	available_ = CreateList(Available, { { L"追加できるソース", 190 }, { L"説明", 260 } });
	add_ = CreateControl(L"BUTTON", L"追加 ↓", WS_TABSTOP, Add);
	remove_ = CreateControl(L"BUTTON", L"削除 ↑", WS_TABSTOP, Remove);
	sources_ = sourceList_.Create(instance_, window_, Sources, &theme_, [this](bool activate) {
		UpdateControls();
		if (activate && !automaticSources_) SendSelection(sources_, sourceRows_, "RemoveSource");
	});
	SetWindowText(sources_, L"使用中のソース");
	SetWindowSubclass(sources_, ControlSubclass, 1, 0);
	postService_ = CreateControl(WC_COMBOBOX, L"", WS_VISIBLE | WS_TABSTOP | CBS_DROPDOWNLIST | CBS_OWNERDRAWFIXED | CBS_HASSTRINGS | WS_VSCROLL, PostService);
	postMail_ = CreateControl(L"EDIT", L"", WS_TABSTOP | WS_BORDER | ES_AUTOHSCROLL, PostMail);
	postText_ = CreateControl(L"EDIT", L"", WS_TABSTOP | WS_BORDER | ES_MULTILINE | ES_AUTOVSCROLL | ES_WANTRETURN | WS_VSCROLL, PostText);
	postThread_ = CreateControl(WC_COMBOBOX, L"", WS_TABSTOP | CBS_DROPDOWNLIST | CBS_OWNERDRAWFIXED | CBS_HASSTRINGS | WS_VSCROLL, PostThread);
	for (HWND combo : { postService_, postThread_ }) TVTCommentPanelDropDown::Attach(combo);
	postButton_ = CreateControl(L"BUTTON", L"投稿", WS_VISIBLE | WS_TABSTOP, PostButton);
	SendMessage(postMail_, EM_SETCUEBANNER, TRUE, reinterpret_cast<LPARAM>(L"コマンド"));
	SendMessage(postService_, CB_SETCUEBANNER, 0, reinterpret_cast<LPARAM>(L"投稿先"));
	SendMessage(postThread_, CB_SETCUEBANNER, 0, reinterpret_cast<LPARAM>(L"スレッド"));
	HWND tooltip = CreateWindowEx(WS_EX_TOPMOST, TOOLTIPS_CLASS, nullptr, WS_POPUP | TTS_ALWAYSTIP,
		CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT, window_, nullptr, instance_, nullptr);
	TOOLINFO tool{ sizeof(tool) };
	tool.uFlags = TTF_IDISHWND | TTF_SUBCLASS;
	tool.hwnd = window_;
	tool.uId = reinterpret_cast<UINT_PTR>(postText_);
	tool.lpszText = const_cast<LPWSTR>(L"コメント (Enterで投稿 / Alt+Enterで改行)");
	SendMessage(tooltip, TTM_ADDTOOL, 0, reinterpret_cast<LPARAM>(&tool));
	status_ = CreateControl(L"STATIC", L"", WS_VISIBLE | SS_CENTERIMAGE | SS_ENDELLIPSIS, Status);
	open_ = CreateControl(L"BUTTON", L"", WS_VISIBLE | WS_TABSTOP, Open);
	// タブの背景を他のコントロールより背面にする
	SetWindowPos(tabs_, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
	UpdateStyle();
	UpdateControls();
	Layout();
}

void TVTCommentPanel::UpdateStyle()
{
	if (!window_) return;
	int oldDpi = dpi_;
	dpi_ = app_->GetDPIFromWindow(window_);
	if (dpi_ <= 0) dpi_ = 96;
	sourceList_.SetDPI(dpi_);
	LOGFONT lf{};
	if (!app_->GetFont(L"panel", &lf, dpi_)) {
		GetObject(GetStockObject(DEFAULT_GUI_FONT), sizeof(lf), &lf);
		lf.lfHeight = -MulDiv(12, dpi_, 96);
	}
	HFONT font = CreateFontIndirect(&lf);
	if (font) {
		for (HWND child = GetWindow(window_, GW_CHILD); child; child = GetWindow(child, GW_HWNDNEXT))
			SendMessage(child, WM_SETFONT, reinterpret_cast<WPARAM>(font), TRUE);
		if (font_) DeleteObject(font_);
		font_ = font;
	}
	for (HWND list : { chats_, channels_, available_ }) {
		for (int column = 0; column < Header_GetItemCount(ListView_GetHeader(list)); ++column)
			ListView_SetColumnWidth(list, column, MulDiv(ListView_GetColumnWidth(list, column), dpi_, oldDpi));
	}
	theme_.Update(window_, dpi_);
}

void TVTCommentPanel::OnColorChange()
{
	// 配色プレビューのキャンセルはイベント終了後に反映
	if (window_) PostMessage(window_, WM_PANEL_THEME_CHANGE, 0, 0);
}

void TVTCommentPanel::UpdateControls()
{
	if (!window_) return;
	SetText(status_, !enabled_ ? L"プラグインは無効です" : !ready_ ? L"WPF側に接続中…" : L"TvtCommentと接続済み");
	SetText(open_, enabled_ ? L"設定ウィンドウ" : L"有効化");
	SetEnabled(open_, !enabled_ || ready_);
	SetEnabled(automatic_, ready_);
	int checked = automaticSources_ ? BST_CHECKED : BST_UNCHECKED;
	if (Button_GetCheck(automatic_) != checked) Button_SetCheck(automatic_, checked);
	SetEnabled(available_, ready_ && !automaticSources_);
	SetEnabled(add_, ready_ && canAddSource_ && !SelectedId(available_, availableRows_).empty());
	SetEnabled(remove_, ready_ && !automaticSources_ && !SelectedId(sources_, sourceRows_).empty());
	bool textMode = PostMode() == L"text", threadMode = PostMode() == L"thread";
	SetEnabled(postService_, ready_ && postingReady_ && !postServiceRows_.empty());
	SetEnabled(postMail_, ready_ && textMode);
	SetEnabled(postText_, ready_ && textMode);
	SetEnabled(postThread_, ready_ && threadMode);
	SetText(postButton_, threadMode ? L"スレを開く" : L"投稿");
	SetEnabled(postButton_, ready_ && postingReady_ && !postPending_ &&
		((textMode && Text(postText_).find_first_not_of(L" \t\r\n") != std::wstring::npos)
			|| (threadMode && !SelectedChoice(postThread_, postThreadRows_).empty())));
}

void TVTCommentPanel::Layout()
{
	if (!window_ || !tabs_) return;
	for (HWND combo : { postService_, postThread_ })
		if (ComboBox_GetDroppedState(combo)) ComboBox_ShowDropdown(combo, FALSE);
	auto px = [this](int n) { return MulDiv(n, dpi_, 96); };
	RECT rect;
	GetClientRect(window_, &rect);
	int margin = px(4), button = px(24), gap = px(3);
	int width = max(0, static_cast<int>(rect.right) - 2 * margin);
	bool threadMode = PostMode() == L"thread", textMode = PostMode() == L"text";
	auto draft = Text(postText_);
	int lines = min(3, 1 + static_cast<int>(std::count(draft.begin(), draft.end(), L'\n')));
	int inputHeight = textMode ? button + px(14) * (lines - 1) : threadMode ? button : 0;
	int height = max(0, static_cast<int>(rect.bottom) - 2 * margin - 2 * button - 2 * gap - (inputHeight ? inputHeight + gap : 0));
	TabCtrl_SetItemSize(tabs_, max(1, (width - px(4)) / 3), px(28));
	MoveWindow(tabs_, margin, margin, width, height, TRUE);
	RECT page{ 0, 0, width, height };
	TabCtrl_AdjustRect(tabs_, FALSE, &page);
	int x = page.left + margin + gap, y = page.top + margin + gap;
	int w = max(0, static_cast<int>(page.right - page.left) - 2 * gap);
	int h = max(0, static_cast<int>(page.bottom - page.top) - 2 * gap);
	auto move = [](HWND control, int x, int y, int w, int h) { MoveWindow(control, x, y, max(0, w), max(0, h), TRUE); };
	int pageIndex = TabCtrl_GetCurSel(tabs_);
	ShowWindow(chats_, pageIndex == ChatPage ? SW_SHOW : SW_HIDE);
	ShowWindow(channels_, pageIndex == ChannelPage ? SW_SHOW : SW_HIDE);
	for (HWND control : { available_, sources_, automatic_, add_, remove_ }) ShowWindow(control, pageIndex == SourcePage ? SW_SHOW : SW_HIDE);
	updatingChats_ = true;
	move(chats_, x, y, w, h);
	updatingChats_ = false;
	FollowChats();
	move(channels_, x, y, w, h);
	move(automatic_, x, y, w, button);
	int half = max(0, (h - 2 * button - 3 * gap) / 2);
	move(available_, x, y + button + gap, w, half);
	int middle = y + button + 2 * gap + half;
	int actionWidth = max(0, min((w - gap) / 2, px(100)));
	int actionsX = x + max(0, (w - 2 * actionWidth - gap) / 2);
	move(add_, actionsX, middle, actionWidth, button);
	move(remove_, actionsX + actionWidth + gap, middle, actionWidth, button);
	move(sources_, x, middle + button + gap, w, half);
	int postingY = margin + height + gap;
	for (HWND combo : { postService_, postThread_ }) {
		ComboBox_SetItemHeight(combo, -1, max(1, button - px(6)));
		ComboBox_SetItemHeight(combo, 0, max(1, button - px(6)));
	}
	move(postService_, margin, postingY, width, px(200));
	postingY += button + gap;
	ShowWindow(postMail_, textMode ? SW_SHOW : SW_HIDE);
	ShowWindow(postText_, textMode ? SW_SHOW : SW_HIDE);
	ShowWindow(postThread_, threadMode ? SW_SHOW : SW_HIDE);
	ShowWindow(postButton_, inputHeight ? SW_SHOW : SW_HIDE);
	int postWidth = min(width, px(threadMode ? 88 : 56));
	int mailWidth = min(width / 3, px(76));
	move(postMail_, margin, postingY, mailWidth, button);
	move(postText_, margin + mailWidth + gap, postingY, width - mailWidth - postWidth - 2 * gap, inputHeight);
	move(postThread_, margin, postingY, width - postWidth - gap, px(200));
	move(postButton_, margin + width - postWidth, postingY, postWidth, button);
	if (inputHeight) postingY += inputHeight + gap;
	int openWidth = min(width, px(116));
	move(status_, margin, postingY, width - openWidth - gap, button);
	move(open_, margin + width - openWidth, postingY, openWidth, button);
}

LRESULT CALLBACK TVTCommentPanel::WindowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
{
	if (message == WM_NCCREATE) {
		auto self = static_cast<TVTCommentPanel *>(reinterpret_cast<CREATESTRUCT *>(lParam)->lpCreateParams);
		SetWindowLongPtr(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
		self->window_ = hwnd;
	}
	auto self = reinterpret_cast<TVTCommentPanel *>(GetWindowLongPtr(hwnd, GWLP_USERDATA));
	return self ? self->HandleWindowMessage(hwnd, message, wParam, lParam) : DefWindowProc(hwnd, message, wParam, lParam);
}

LRESULT TVTCommentPanel::HandleWindowMessage(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
{
	LRESULT themedResult;
	if (theme_.HandleMessage(hwnd, message, wParam, lParam, themedResult)) return themedResult;
	switch (message) {
	case WM_CREATE:
		CreateControls();
		SetTimer(hwnd, VISIBILITY_TIMER, 250, nullptr);
		return 0;
	case WM_TIMER:
		if (wParam == VISIBILITY_TIMER) { UpdateVisibility(); return 0; }
		break;
	case WM_SIZE:
		Layout();
		return 0;
	case WM_THEMECHANGED:
	case WM_SYSCOLORCHANGE:
	case WM_PANEL_THEME_CHANGE:
		theme_.Update(hwnd, dpi_);
		return 0;
	case WM_SHOWWINDOW:
		Subscribe(active_ && wParam != 0);
		break;
	case WM_SETFOCUS:
		SetFocus(TabCtrl_GetCurSel(tabs_) == ChannelPage ? channels_ : TabCtrl_GetCurSel(tabs_) == ChatPage ? chats_ : available_);
		return 0;
	case WM_COMMAND:
		if ((LOWORD(wParam) == PostText || LOWORD(wParam) == PostMail) && HIWORD(wParam) == EN_CHANGE) {
			SendPostDraft(false);
			if (!updatingPost_ && LOWORD(wParam) == PostText) Layout();
			return 0;
		}
		if ((LOWORD(wParam) == PostService || LOWORD(wParam) == PostThread) && HIWORD(wParam) == CBN_SELCHANGE) {
			if (LOWORD(wParam) == PostService) ComboBox_SetCurSel(postThread_, -1);
			Layout();
			SendPostDraft(false);
			return 0;
		}
		if (LOWORD(wParam) == PostThread && HIWORD(wParam) == CBN_DROPDOWN) { Send("RefreshPostThreads"); return 0; }
		if (HIWORD(wParam) != BN_CLICKED) break;
		switch (LOWORD(wParam)) {
		case Open:
			if (!enabled_) app_->EnablePlugin(true);
			else Send("ShowWindow");
			break;
		case PostButton: SendPostDraft(true); break;
		case Add: if (canAddSource_) SendSelection(available_, availableRows_, "AddSource"); break;
		case Remove: if (!automaticSources_) SendSelection(sources_, sourceRows_, "RemoveSource"); break;
		case Automatic: Send("AutoSources", { Button_GetCheck(automatic_) == BST_CHECKED ? "1" : "0" }); break;
		}
		return 0;
	case WM_NOTIFY:
	{
		auto header = reinterpret_cast<NMHDR *>(lParam);
		if (header->hwndFrom == chats_ && header->code == LVN_ENDSCROLL && !updatingChats_) followChats_ = ChatsAtBottom();
		if (header->hwndFrom == tabs_ && header->code == TCN_SELCHANGE) { Layout(); return 0; }
		const Rows *rows = header->hwndFrom == chats_ ? &chatRows_ : header->hwndFrom == channels_ ? &channelRows_
			: header->hwndFrom == available_ ? &availableRows_ : header->hwndFrom == sources_ ? &sourceRows_ : nullptr;
		if (!rows) break;
		if (header->code == LVN_GETDISPINFO) {
			auto info = reinterpret_cast<NMLVDISPINFO *>(lParam);
			if ((info->item.mask & LVIF_TEXT) && info->item.iItem >= 0 && static_cast<size_t>(info->item.iItem) < rows->size()) {
				const auto &cells = (*rows)[info->item.iItem].Cells;
				if (info->item.iSubItem >= 0 && static_cast<size_t>(info->item.iSubItem) < cells.size()) {
					auto text = cells[info->item.iSubItem];
					std::replace(text.begin(), text.end(), L'\r', L' ');
					std::replace(text.begin(), text.end(), L'\n', L' ');
					lstrcpyn(info->item.pszText, text.c_str(), info->item.cchTextMax);
				}
			}
			return 0;
		}
		if (header->code == LVN_GETINFOTIP) {
			auto info = reinterpret_cast<NMLVGETINFOTIP *>(lParam);
			if (info->iItem >= 0 && static_cast<size_t>(info->iItem) < rows->size()) {
				std::wstring text;
				for (const auto &cell : (*rows)[info->iItem].Cells) text += cell + L"\r\n";
				lstrcpyn(info->pszText, text.c_str(), info->cchTextMax);
			}
			return 0;
		}
		if (header->code == LVN_ITEMCHANGED || header->code == LVN_ODSTATECHANGED) { UpdateControls(); return 0; }
		bool activate = header->code == NM_DBLCLK || (header->code == LVN_KEYDOWN && reinterpret_cast<NMLVKEYDOWN *>(lParam)->wVKey == VK_RETURN);
		if (activate) {
			if (header->hwndFrom == channels_) SendSelection(channels_, channelRows_, "SelectChannel");
			else if (header->hwndFrom == available_ && canAddSource_) SendSelection(available_, availableRows_, "AddSource");
			else if (header->hwndFrom == sources_ && !automaticSources_) SendSelection(sources_, sourceRows_, "RemoveSource");
			else if (header->hwndFrom == chats_) {
				int selected = ListView_GetNextItem(chats_, -1, LVNI_SELECTED);
				if (selected >= 0 && static_cast<size_t>(selected) < chatRows_.size()) {
					const auto &cells = chatRows_[selected].Cells;
					std::wstring text = cells[0] + L"  " + cells[2] + L"\r\nユーザー: " + cells[3] + L"\r\nコメ番: " + cells[4] + L"\r\n\r\n" + cells[1];
					MessageBox(hwnd, text.c_str(), L"コメント詳細", MB_OK);
				}
			}
			return 0;
		}
		break;
	}
	case WM_DESTROY:
		active_ = false;
		UpdateVisibility();
		KillTimer(hwnd, VISIBILITY_TIMER);
		Subscribe(false);
		return 0;
	case WM_NCDESTROY:
		window_ = nullptr;
		dpi_ = 96; // 再作成時の列幅は96 DPI基準
		tabs_ = status_ = open_ = chats_ = channels_ = available_ = sources_ = automatic_ = add_ = remove_ = nullptr;
		postService_ = postMail_ = postText_ = postThread_ = postButton_ = nullptr;
		postServiceRows_.clear();
		postThreadRows_.clear();
		SetWindowLongPtr(hwnd, GWLP_USERDATA, 0);
		break;
	}
	return DefWindowProc(hwnd, message, wParam, lParam);
}
