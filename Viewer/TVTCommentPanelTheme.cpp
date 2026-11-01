#include "stdafx.h"
#include "TVTCommentPanelTheme.h"
#include "TVTCommentPanelSourceList.h"

namespace
{
	// SDK 0.0.14にないダークモード関連のメッセージ
	constexpr UINT GET_DARK_MODE_STATUS = TVTest::MESSAGE_REGISTERVARIABLE + 1;
	constexpr UINT IS_DARK_MODE_COLOR = TVTest::MESSAGE_REGISTERVARIABLE + 2;
	constexpr UINT SET_WINDOW_DARK_MODE = TVTest::MESSAGE_REGISTERVARIABLE + 3;
	constexpr DWORD PANEL_DARK_MODE_SUPPORTED = 0x4;
	const wchar_t HOT_PROPERTY[] = L"TVTComment.Panel.Hot";

	bool IsClass(HWND window, LPCWSTR name)
	{
		wchar_t buffer[64]{};
		GetClassName(window, buffer, _countof(buffer));
		return lstrcmpi(buffer, name) == 0;
	}
	void Fill(HDC dc, const RECT &rect, COLORREF color)
	{
		SetDCBrushColor(dc, color);
		FillRect(dc, &rect, static_cast<HBRUSH>(GetStockObject(DC_BRUSH)));
	}
	void Frame(HDC dc, const RECT &rect, COLORREF color)
	{
		SetDCBrushColor(dc, color);
		FrameRect(dc, &rect, static_cast<HBRUSH>(GetStockObject(DC_BRUSH)));
	}
	COLORREF Blend(COLORREF a, COLORREF b)
	{
		return RGB((GetRValue(a) + GetRValue(b)) / 2, (GetGValue(a) + GetGValue(b)) / 2, (GetBValue(a) + GetBValue(b)) / 2);
	}
	int Contrast(COLORREF a, COLORREF b)
	{
		return max({ abs(GetRValue(a) - GetRValue(b)), abs(GetGValue(a) - GetGValue(b)), abs(GetBValue(a) - GetBValue(b)) });
	}
}

TVTCommentPanelTheme::~TVTCommentPanelTheme()
{
	if (backgroundBrush_) DeleteObject(backgroundBrush_);
	if (inputBrush_) DeleteObject(inputBrush_);
}

void TVTCommentPanelTheme::Initialize(TVTest::CTVTestApp *app, TVTest::PluginParam *parameter)
{
	app_ = app;
	parameter_ = parameter;
}

COLORREF TVTCommentPanelTheme::Color(LPCWSTR name, COLORREF fallback) const
{
	COLORREF color = app_->GetColor(name);
	return color == CLR_INVALID ? fallback : color;
}

void TVTCommentPanelTheme::Update(HWND window, int dpi)
{
	if (updating_) return; // WM_THEMECHANGEDの再入を防ぐ
	updating_ = true;
	dpi_ = dpi;
	background_ = Color(L"PanelBack", GetSysColor(COLOR_BTNFACE));
	text_ = Color(L"PanelText", GetSysColor(COLOR_BTNTEXT));
	input_ = Color(L"ProgramInfoBack", background_);
	inputText_ = Color(L"ProgramInfoText", text_);
	border_ = Color(L"PanelTabLine", Blend(background_, text_));
	if (Contrast(border_, background_) < 40 || Contrast(border_, input_) < 40)
		border_ = Blend(background_, text_);
	selection_ = Color(L"ControlPanelHighlightBack", GetSysColor(COLOR_HIGHLIGHT));
	selectionText_ = Color(L"ControlPanelHighlightText", GetSysColor(COLOR_HIGHLIGHTTEXT));
	disabled_ = Blend(text_, background_);
	if (backgroundBrush_) DeleteObject(backgroundBrush_);
	if (inputBrush_) DeleteObject(inputBrush_);
	backgroundBrush_ = CreateSolidBrush(background_);
	inputBrush_ = CreateSolidBrush(input_);
	dark_ = false;
	if (parameter_ && app_->QueryMessage(SET_WINDOW_DARK_MODE)) {
		bool supported = (parameter_->Callback(parameter_, GET_DARK_MODE_STATUS, 0, 0) & PANEL_DARK_MODE_SUPPORTED) != 0;
		dark_ = supported && parameter_->Callback(parameter_, IS_DARK_MODE_COLOR, background_, 0) != 0;
		parameter_->Callback(parameter_, SET_WINDOW_DARK_MODE, reinterpret_cast<LPARAM>(window), dark_);
	}
	for (HWND child = GetWindow(window, GW_CHILD); child; child = GetWindow(child, GW_HWNDNEXT)) {
		ApplyControl(child);
		if (IsClass(child, WC_LISTVIEW)) {
			ListView_SetBkColor(child, input_);
			ListView_SetTextBkColor(child, input_);
			ListView_SetTextColor(child, inputText_);
			ApplyControl(ListView_GetHeader(child));
		}
		if (IsClass(child, WC_COMBOBOX)) {
			COMBOBOXINFO info{ sizeof(info) };
			if (GetComboBoxInfo(child, &info)) ApplyControl(info.hwndList);
		}
	}
	RedrawWindow(window, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_FRAME);
	updating_ = false;
}

void TVTCommentPanelTheme::ApplyControl(HWND control)
{
	if (!control) return;
	if (parameter_ && app_->QueryMessage(SET_WINDOW_DARK_MODE))
		parameter_->Callback(parameter_, SET_WINDOW_DARK_MODE, reinterpret_cast<LPARAM>(control), dark_);
	SetWindowSubclass(control, ControlSubclass, 10, reinterpret_cast<DWORD_PTR>(this));
}

void TVTCommentPanelTheme::Background(HDC dc, const RECT &rect, LPCWSTR style, COLORREF fallback)
{
	Fill(dc, rect, fallback);
	app_->ThemeDrawBackground(style, dc, rect, dpi_);
}

void TVTCommentPanelTheme::Label(HDC dc, RECT rect, LPCWSTR text, COLORREF color, UINT format)
{
	if (IsRectEmpty(&rect)) return;
	SetBkMode(dc, TRANSPARENT);
	SetTextColor(dc, color);
	DrawText(dc, text, -1, &rect, format);
}

void TVTCommentPanelTheme::PaintBorder(HWND control, HDC dc)
{
	RECT rect;
	GetWindowRect(control, &rect);
	OffsetRect(&rect, -rect.left, -rect.top);
	Frame(dc, rect, GetFocus() == control ? Color(L"PanelCurTabText", text_) : border_);
}

void TVTCommentPanelTheme::PaintControlBuffered(HWND control, HDC dc)
{
	RECT rect;
	GetClientRect(control, &rect);
	if (rect.right <= 0 || rect.bottom <= 0) return;
	HDC buffer = CreateCompatibleDC(dc);
	HBITMAP bitmap = CreateCompatibleBitmap(dc, rect.right, rect.bottom);
	if (buffer && bitmap) {
		auto old = SelectObject(buffer, bitmap);
		// ヘッダーや他のコントロールの領域を残す
		BitBlt(buffer, 0, 0, rect.right, rect.bottom, dc, 0, 0, SRCCOPY);
		PaintControl(control, buffer);
		BitBlt(dc, 0, 0, rect.right, rect.bottom, buffer, 0, 0, SRCCOPY);
		SelectObject(buffer, old);
	} else PaintControl(control, dc);
	if (bitmap) DeleteObject(bitmap);
	if (buffer) DeleteDC(buffer);
}

void TVTCommentPanelTheme::PaintControl(HWND control, HDC dc)
{
	int saved = SaveDC(dc);
	auto font = reinterpret_cast<HFONT>(SendMessage(control, WM_GETFONT, 0, 0));
	if (font) SelectObject(dc, font);
	RECT rect;
	GetClientRect(control, &rect);
	bool enabled = IsWindowEnabled(control) != FALSE;
	bool hot = enabled && GetProp(control, HOT_PROPERTY);
	wchar_t text[1024]{};
	GetWindowText(control, text, _countof(text));
	if (IsClass(control, WC_LISTVIEW)) {
		// 無効なリストは標準描画だとシステム色になる
		RECT header;
		GetWindowRect(ListView_GetHeader(control), &header);
		MapWindowPoints(nullptr, control, reinterpret_cast<POINT *>(&header), 2);
		ExcludeClipRect(dc, header.left, header.top, header.right, header.bottom);
		Fill(dc, rect, input_);
		int first = ListView_GetTopIndex(control);
		int end = min(ListView_GetItemCount(control), first + ListView_GetCountPerPage(control) + 1);
		for (int row = first; row < end; ++row) DrawRow(control, dc, row, false);
		DrawListDividers(control, dc);
	} else if (IsClass(control, WC_TABCONTROL)) {
		// WM_PRINTCLIENTで他のコントロールを塗りつぶさないようにする
		for (HWND sibling = GetWindow(GetParent(control), GW_CHILD); sibling; sibling = GetWindow(sibling, GW_HWNDNEXT)) {
			if (sibling == control || !(GetWindowLongPtr(sibling, GWL_STYLE) & WS_VISIBLE)) continue;
			RECT bounds;
			GetWindowRect(sibling, &bounds);
			MapWindowPoints(nullptr, control, reinterpret_cast<POINT *>(&bounds), 2);
			ExcludeClipRect(dc, bounds.left, bounds.top, bounds.right, bounds.bottom);
		}
		Fill(dc, rect, background_);
		RECT page = rect;
		TabCtrl_AdjustRect(control, FALSE, &page);
		RECT frame{ rect.left, page.top - Px(2), rect.right, rect.bottom };
		Frame(dc, frame, border_);
		for (int i = 0; i < TabCtrl_GetItemCount(control); ++i) {
			RECT item;
			TabCtrl_GetItemRect(control, i, &item);
			bool selected = i == TabCtrl_GetCurSel(control);
			item.bottom = frame.top + 1;
			Background(dc, item, selected ? L"panel.tab.current" : L"panel.tab", background_);
			Frame(dc, item, border_);
			COLORREF textColor = Color(selected ? L"PanelCurTabText" : L"PanelTabText", text_);
			if (selected) {
				RECT accent{ item.left + 1, item.top, item.right - 1, item.top + Px(2) };
				Fill(dc, accent, textColor);
				RECT opening{ item.left + 1, frame.top, item.right - 1, frame.top + 1 };
				Fill(dc, opening, background_);
			}
			TCITEM tab{};
			tab.mask = TCIF_TEXT;
			tab.pszText = text;
			tab.cchTextMax = _countof(text);
			TabCtrl_GetItem(control, i, &tab);
			Label(dc, item, text, textColor, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS | DT_NOPREFIX);
			if (selected && GetFocus() == control) { InflateRect(&item, -Px(3), -Px(3)); DrawFocusRect(dc, &item); }
		}
	} else if (IsClass(control, WC_HEADER)) {
		Background(dc, rect, L"panel.tab", background_);
		for (int i = 0; i < Header_GetItemCount(control); ++i) {
			RECT item;
			Header_GetItemRect(control, i, &item);
			RECT divider{ item.right - 1, item.top, item.right, item.bottom };
			HDITEM header{};
			header.mask = HDI_TEXT;
			header.pszText = text;
			header.cchTextMax = _countof(text);
			Header_GetItem(control, i, &header);
			item.left += Px(4);
			item.right -= Px(4);
			Label(dc, item, text, Color(L"PanelTabText", text_), DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS | DT_NOPREFIX);
			Fill(dc, divider, border_);
		}
		RECT line{ rect.left, rect.bottom - 1, rect.right, rect.bottom };
		Fill(dc, line, border_);
	} else if (IsClass(control, WC_COMBOBOX)) {
		Fill(dc, rect, input_);
		RECT arrow = rect;
		arrow.left = max(0L, arrow.right - Px(20));
		Background(dc, arrow, hot ? L"control-panel.item.hot" : L"control-panel.item", background_);
		Label(dc, arrow, L"\x25BE", !enabled ? disabled_ : hot ? selectionText_ : text_, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
		RECT divider{ arrow.left, arrow.top + 1, arrow.left + 1, arrow.bottom - 1 };
		Fill(dc, divider, border_);
		Frame(dc, rect, hot || GetFocus() == control ? Color(L"PanelCurTabText", text_) : border_);
		rect.right = arrow.left - Px(3);
		rect.left += Px(4);
		if (!text[0]) SendMessage(control, CB_GETCUEBANNER, reinterpret_cast<WPARAM>(text), _countof(text));
		Label(dc, rect, text, enabled ? inputText_ : disabled_);
	} else {
		bool checkbox = (GetWindowLongPtr(control, GWL_STYLE) & BS_TYPEMASK) == BS_AUTOCHECKBOX;
		bool checked = Button_GetCheck(control) == BST_CHECKED;
		bool pushed = (Button_GetState(control) & BST_PUSHED) != 0;
		if (checkbox) {
			Fill(dc, rect, background_);
			RECT box{ Px(2), (rect.bottom - Px(13)) / 2, Px(15), (rect.bottom + Px(13)) / 2 };
			Fill(dc, box, checked ? selection_ : input_);
			Frame(dc, box, hot ? selection_ : border_);
			if (checked) Label(dc, box, L"\x2713", enabled ? selectionText_ : disabled_, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
			rect.left = box.right + Px(5);
			Label(dc, rect, text, enabled ? text_ : disabled_);
		} else {
			Background(dc, rect, pushed ? L"control-panel.item.checked" : hot ? L"control-panel.item.hot" : L"control-panel.item", background_);
			Frame(dc, rect, hot || GetFocus() == control ? Color(L"PanelCurTabText", text_) : border_);
			Label(dc, rect, text, !enabled ? disabled_ : Color(pushed ? L"ControlPanelCheckedText" : hot ? L"ControlPanelHighlightText" : L"ControlPanelText", text_),
				DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS | DT_NOPREFIX);
		}
		if (GetFocus() == control) { InflateRect(&rect, -Px(3), -Px(3)); DrawFocusRect(dc, &rect); }
	}
	RestoreDC(dc, saved);
}

void TVTCommentPanelTheme::DrawComboItem(const DRAWITEMSTRUCT &item)
{
	bool selected = (item.itemState & ODS_SELECTED) != 0 && !(item.itemState & ODS_COMBOBOXEDIT);
	Fill(item.hDC, item.rcItem, selected ? selection_ : input_);
	if (item.itemID == static_cast<UINT>(-1)) return;
	int length = ComboBox_GetLBTextLen(item.hwndItem, item.itemID);
	if (length < 0) return;
	std::wstring text(length + 1, L'\0');
	ComboBox_GetLBText(item.hwndItem, item.itemID, &text[0]);
	RECT rect = item.rcItem;
	rect.left += Px(4);
	Label(item.hDC, rect, text.c_str(), (item.itemState & ODS_DISABLED) ? disabled_ : selected ? selectionText_ : inputText_);
}

void TVTCommentPanelTheme::DrawRow(HWND list, HDC dc, int row, bool selected)
{
	RECT bounds;
	ListView_GetItemRect(list, row, &bounds, LVIR_BOUNDS);
	Fill(dc, bounds, selected ? selection_ : input_);
	for (int column = 0; column < Header_GetItemCount(ListView_GetHeader(list)); ++column) {
		RECT cell;
		ListView_GetSubItemRect(list, row, column, LVIR_BOUNDS, &cell);
		if (column == 0) cell.right = cell.left + ListView_GetColumnWidth(list, 0);
		wchar_t text[2048]{};
		ListView_GetItemText(list, row, column, text, _countof(text));
		cell.left += Px(5);
		cell.right -= Px(3);
		Label(dc, cell, text, !IsWindowEnabled(list) ? disabled_ : selected ? selectionText_ : inputText_);
	}
	if (GetFocus() == list && (ListView_GetItemState(list, row, LVIS_FOCUSED) & LVIS_FOCUSED)) DrawFocusRect(dc, &bounds);
}

void TVTCommentPanelTheme::DrawListDividers(HWND list, HDC dc)
{
	HWND header = ListView_GetHeader(list);
	RECT client{}, headerBounds{};
	GetClientRect(list, &client);
	if (!GetWindowRect(header, &headerBounds)) return;
	MapWindowPoints(nullptr, list, reinterpret_cast<POINT *>(&headerBounds), 2);
	client.top = max(client.top, headerBounds.bottom);
	if (IsRectEmpty(&client)) return;

	// 文字・選択背景の描画後に、空白部分まで共通の罫線を重ねる
	for (int column = 0; column < Header_GetItemCount(header); ++column) {
		RECT item{};
		if (!Header_GetItemRect(header, column, &item) || IsRectEmpty(&item)) continue;
		MapWindowPoints(header, list, reinterpret_cast<POINT *>(&item), 2);
		if (item.right <= client.left || item.right > client.right) continue;
		RECT divider{ item.right - 1, client.top, item.right, client.bottom };
		Fill(dc, divider, border_);
	}
}

bool TVTCommentPanelTheme::HandleMessage(HWND, UINT message, WPARAM wParam, LPARAM lParam, LRESULT &result)
{
	result = 0;
	if (message == WM_ERASEBKGND && backgroundBrush_) {
		RECT rect;
		GetClipBox(reinterpret_cast<HDC>(wParam), &rect);
		FillRect(reinterpret_cast<HDC>(wParam), &rect, backgroundBrush_);
		result = 1;
		return true;
	}
	if (message == WM_CTLCOLORSTATIC || message == WM_CTLCOLOREDIT || message == WM_CTLCOLORLISTBOX || message == WM_CTLCOLORBTN) {
		auto control = reinterpret_cast<HWND>(lParam);
		bool input = message == WM_CTLCOLOREDIT || message == WM_CTLCOLORLISTBOX || IsClass(control, L"EDIT") || IsClass(control, WC_COMBOBOX);
		auto dc = reinterpret_cast<HDC>(wParam);
		SetTextColor(dc, !IsWindowEnabled(control) ? disabled_ : input ? inputText_ : text_);
		SetBkColor(dc, input ? input_ : background_);
		result = reinterpret_cast<LRESULT>(input ? inputBrush_ : backgroundBrush_);
		return result != 0;
	}
	if (message == WM_MEASUREITEM) {
		auto item = reinterpret_cast<MEASUREITEMSTRUCT *>(lParam);
		if (item->CtlType == ODT_COMBOBOX) { item->itemHeight = Px(18); result = TRUE; return true; }
	}
	if (message == WM_DRAWITEM) {
		auto item = reinterpret_cast<DRAWITEMSTRUCT *>(lParam);
		if (item->CtlType == ODT_COMBOBOX) { DrawComboItem(*item); result = TRUE; return true; }
	}
	if (message == WM_NOTIFY) {
		auto header = reinterpret_cast<NMHDR *>(lParam);
		if (header->code == NM_CUSTOMDRAW && (IsClass(header->hwndFrom, L"BUTTON") || IsClass(header->hwndFrom, WC_HEADER))) {
			auto draw = reinterpret_cast<NMCUSTOMDRAW *>(lParam);
			if (draw->dwDrawStage == CDDS_PREPAINT) {
				// ホバー時はWM_PAINT以外でも描画される
				PaintControlBuffered(header->hwndFrom, draw->hdc);
				result = CDRF_SKIPDEFAULT;
			} else result = CDRF_DODEFAULT;
			return true;
		}
		if (header->code == NM_CUSTOMDRAW && IsClass(header->hwndFrom, WC_LISTVIEW)) {
			auto draw = reinterpret_cast<NMLVCUSTOMDRAW *>(lParam);
			if (draw->nmcd.dwDrawStage == CDDS_PREPAINT) result = CDRF_NOTIFYITEMDRAW | CDRF_NOTIFYPOSTPAINT;
			else if (draw->nmcd.dwDrawStage == CDDS_ITEMPREPAINT) {
				int row = static_cast<int>(draw->nmcd.dwItemSpec);
				bool selected = (ListView_GetItemState(header->hwndFrom, row, LVIS_SELECTED) & LVIS_SELECTED) != 0;
				DrawRow(header->hwndFrom, draw->nmcd.hdc, row, selected);
				result = CDRF_SKIPDEFAULT;
			} else if (draw->nmcd.dwDrawStage == CDDS_POSTPAINT) {
				DrawListDividers(header->hwndFrom, draw->nmcd.hdc);
				result = CDRF_DODEFAULT;
			} else result = CDRF_DODEFAULT;
			return true;
		}
	}
	return false;
}

LRESULT CALLBACK TVTCommentPanelTheme::ControlSubclass(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR id, DWORD_PTR data)
{
	auto self = reinterpret_cast<TVTCommentPanelTheme *>(data);
	bool listView = IsClass(hwnd, WC_LISTVIEW);
	bool bordered = listView || IsClass(hwnd, L"EDIT") || IsClass(hwnd, TVTCommentPanelSourceList::ClassName());
	bool hover = IsClass(hwnd, L"BUTTON") || IsClass(hwnd, WC_COMBOBOX);
	bool painted = IsClass(hwnd, WC_TABCONTROL) || IsClass(hwnd, WC_HEADER) || IsClass(hwnd, L"BUTTON") || IsClass(hwnd, WC_COMBOBOX)
		|| (listView && !IsWindowEnabled(hwnd));
	if (painted && message == WM_ERASEBKGND) return 1;
	if (painted && (message == WM_PAINT || message == WM_PRINTCLIENT)) {
		PAINTSTRUCT paint{};
		HDC dc = message == WM_PAINT ? BeginPaint(hwnd, &paint) : reinterpret_cast<HDC>(wParam);
		self->PaintControlBuffered(hwnd, dc);
		if (message == WM_PAINT) EndPaint(hwnd, &paint);
		return 0;
	}
	LRESULT result;
	if ((message == WM_CTLCOLORLISTBOX || message == WM_NOTIFY) && self->HandleMessage(hwnd, message, wParam, lParam, result)) return result;
	if (hover && message == WM_MOUSEMOVE && !GetProp(hwnd, HOT_PROPERTY)) {
		SetProp(hwnd, HOT_PROPERTY, reinterpret_cast<HANDLE>(1));
		TRACKMOUSEEVENT track{ sizeof(track), TME_LEAVE, hwnd, 0 };
		TrackMouseEvent(&track);
		InvalidateRect(hwnd, nullptr, FALSE);
	}
	if (hover && message == WM_MOUSELEAVE) { RemoveProp(hwnd, HOT_PROPERTY); InvalidateRect(hwnd, nullptr, FALSE); }
	// ドラッグ中以外は標準のホバー描画を抑制
	if ((hover || IsClass(hwnd, WC_TABCONTROL)) && GetCapture() != hwnd && (message == WM_MOUSEMOVE || message == WM_MOUSELEAVE)) return 0;
	int oldHorizontalPosition = listView ? GetScrollPos(hwnd, SB_HORZ) : 0;
	result = DefSubclassProc(hwnd, message, wParam, lParam);
	if (listView && message != WM_NCDESTROY && GetScrollPos(hwnd, SB_HORZ) != oldHorizontalPosition) {
		// 標準のスクロール描画は既存の画素を転送するため、独自の罫線が残る
		// ヘッダーの移動完了後に空白部分も含めて描き直す
		RedrawWindow(hwnd, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
	}
	if (bordered && message == WM_NCPAINT) {
		HDC dc = GetWindowDC(hwnd);
		if (dc) { self->PaintBorder(hwnd, dc); ReleaseDC(hwnd, dc); }
	}
	if (bordered && message == WM_PRINT && (lParam & PRF_NONCLIENT)) self->PaintBorder(hwnd, reinterpret_cast<HDC>(wParam));
	if (bordered && (message == WM_SETFOCUS || message == WM_KILLFOCUS || message == WM_ENABLE))
		RedrawWindow(hwnd, nullptr, nullptr, RDW_INVALIDATE | RDW_FRAME);
	if (message == WM_ENABLE || (painted && (message == WM_SETFOCUS || message == WM_KILLFOCUS || message == BM_SETCHECK || message == BM_SETSTATE || message == CB_SETCURSEL)))
		InvalidateRect(hwnd, nullptr, FALSE);
	if (message == WM_NCDESTROY) {
		RemoveProp(hwnd, HOT_PROPERTY);
		RemoveWindowSubclass(hwnd, ControlSubclass, id);
	}
	return result;
}
