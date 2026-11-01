#include "stdafx.h"
#include "TVTCommentPanelSourceList.h"

namespace
{
	constexpr UINT TEXT_FORMAT = DT_LEFT | DT_TOP | DT_NOPREFIX | DT_EXPANDTABS;
	void Fill(HDC dc, const RECT &rect, COLORREF color)
	{
		SetDCBrushColor(dc, color);
		FillRect(dc, &rect, static_cast<HBRUSH>(GetStockObject(DC_BRUSH)));
	}
}

HWND TVTCommentPanelSourceList::Create(HINSTANCE instance, HWND parent, int id, TVTCommentPanelTheme *theme, std::function<void(bool)> notify)
{
	instance_ = instance;
	theme_ = theme;
	notify_ = std::move(notify);
	WNDCLASSEX wc{ sizeof(wc) };
	wc.hInstance = instance;
	wc.lpfnWndProc = WindowProc;
	wc.lpszClassName = ClassName();
	wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
	wc.style = CS_DBLCLKS;
	if (!RegisterClassEx(&wc) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) return nullptr;
	return CreateWindowEx(0, ClassName(), L"", WS_CHILD | WS_TABSTOP | WS_BORDER | WS_VSCROLL | WS_HSCROLL,
		0, 0, 0, 0, parent, reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)), instance, this);
}

void TVTCommentPanelSourceList::Finalize()
{
	if (window_) DestroyWindow(window_);
	if (instance_) UnregisterClass(ClassName(), instance_);
	instance_ = nullptr;
	notify_ = {};
}

std::string TVTCommentPanelSourceList::SelectedId() const
{
	return selected_ >= 0 && selected_ < static_cast<int>(items_.size()) ? items_[selected_].Id : std::string();
}

void TVTCommentPanelSourceList::SetItems(std::vector<Item> items)
{
	bool changed = items.size() != items_.size();
	for (size_t i = 0; !changed && i < items.size(); ++i)
		changed = items[i].Id != items_[i].Id || items[i].Name != items_[i].Name || items[i].Details != items_[i].Details;
	if (!changed) return;
	auto selected = SelectedId();
	int top = HitTest(0);
	std::string anchor = top >= 0 ? items_[top].Id : std::string();
	int offset = top >= 0 ? y_ - layout_[top].Top : 0;
	items_ = std::move(items);
	selected_ = -1;
	for (size_t i = 0; i < items_.size(); ++i) if (items_[i].Id == selected) selected_ = static_cast<int>(i);
	Measure();
	for (size_t i = 0; i < layout_.size(); ++i)
		if (items_[i].Id == anchor) { Scroll(SB_VERT, layout_[i].Top + min(offset, layout_[i].Height - 1)); break; }
}

void TVTCommentPanelSourceList::SetDPI(int dpi)
{
	dpi_ = dpi;
	Measure();
}

void TVTCommentPanelSourceList::Measure()
{
	layout_.clear();
	width_ = height_ = 0;
	if (!window_) return;
	HDC dc = GetDC(window_);
	auto old = SelectObject(dc, font_ ? font_ : GetStockObject(DEFAULT_GUI_FONT));
	TEXTMETRIC metrics{};
	GetTextMetrics(dc, &metrics);
	lineHeight_ = max(1L, metrics.tmHeight);
	for (const auto &item : items_) {
		RECT name{}, details{};
		if (boldFont_) SelectObject(dc, boldFont_);
		DrawText(dc, item.Name.c_str(), -1, &name, TEXT_FORMAT | DT_CALCRECT);
		SelectObject(dc, font_ ? font_ : GetStockObject(DEFAULT_GUI_FONT));
		if (!item.Details.empty()) DrawText(dc, item.Details.c_str(), -1, &details, TEXT_FORMAT | DT_CALCRECT);
		int nameHeight = max(lineHeight_, static_cast<int>(name.bottom));
		int height = nameHeight + static_cast<int>(details.bottom) + Px(8) + 1;
		layout_.push_back({ height_, height, nameHeight });
		height_ += height;
		width_ = max(width_, static_cast<int>(max(name.right, details.right)) + Px(12));
	}
	SelectObject(dc, old);
	ReleaseDC(window_, dc);
	UpdateScrollbars();
}

void TVTCommentPanelSourceList::UpdateScrollbars()
{
	if (!window_ || updatingScroll_) return;
	updatingScroll_ = true;
	// スクロールバーの表示でクライアント領域が変わる
	for (int pass = 0; pass < 3; ++pass) {
		RECT client;
		GetClientRect(window_, &client);
		SCROLLINFO horizontal{ sizeof(horizontal), SIF_RANGE | SIF_PAGE | SIF_POS, 0, max(0, width_ - 1), static_cast<UINT>(max(1L, client.right)), x_ };
		SCROLLINFO vertical{ sizeof(vertical), SIF_RANGE | SIF_PAGE | SIF_POS, 0, max(0, height_ - 1), static_cast<UINT>(max(1L, client.bottom)), y_ };
		SetScrollInfo(window_, SB_HORZ, &horizontal, TRUE);
		SetScrollInfo(window_, SB_VERT, &vertical, TRUE);
		x_ = GetScrollPos(window_, SB_HORZ);
		y_ = GetScrollPos(window_, SB_VERT);
	}
	updatingScroll_ = false;
	InvalidateRect(window_, nullptr, FALSE);
}

void TVTCommentPanelSourceList::Scroll(int bar, int position)
{
	SCROLLINFO info{ sizeof(info), SIF_POS };
	info.nPos = position;
	SetScrollInfo(window_, bar, &info, TRUE);
	(bar == SB_VERT ? y_ : x_) = GetScrollPos(window_, bar);
	InvalidateRect(window_, nullptr, FALSE);
}

void TVTCommentPanelSourceList::ScrollCommand(int bar, UINT command)
{
	SCROLLINFO info{ sizeof(info), SIF_ALL };
	GetScrollInfo(window_, bar, &info);
	int position = info.nPos;
	switch (command) {
	case SB_LINEUP: position -= lineHeight_; break;
	case SB_LINEDOWN: position += lineHeight_; break;
	case SB_PAGEUP: position -= info.nPage; break;
	case SB_PAGEDOWN: position += info.nPage; break;
	case SB_TOP: position = 0; break;
	case SB_BOTTOM: position = info.nMax; break;
	case SB_THUMBTRACK:
	case SB_THUMBPOSITION: position = info.nTrackPos; break;
	default: return;
	}
	Scroll(bar, position);
}

int TVTCommentPanelSourceList::HitTest(int y) const
{
	for (size_t i = 0; i < layout_.size(); ++i)
		if (y + y_ >= layout_[i].Top && y + y_ < layout_[i].Top + layout_[i].Height) return static_cast<int>(i);
	return -1;
}

RECT TVTCommentPanelSourceList::ItemRect(int index) const
{
	RECT client;
	GetClientRect(window_, &client);
	const auto &item = layout_[index];
	return { 0, item.Top - y_, max(client.right, static_cast<LONG>(width_)), item.Top - y_ + item.Height };
}

std::wstring TVTCommentPanelSourceList::ItemText(int index) const
{
	return items_[index].Name + L"\n" + items_[index].Details;
}

void TVTCommentPanelSourceList::Select(int index, bool ensureVisible, bool notify)
{
	selected_ = index >= 0 && index < static_cast<int>(items_.size()) ? index : -1;
	if (ensureVisible && selected_ >= 0) {
		RECT client;
		GetClientRect(window_, &client);
		const auto &item = layout_[selected_];
		if (item.Top < y_ || item.Height > client.bottom) Scroll(SB_VERT, item.Top);
		else if (item.Top + item.Height > y_ + client.bottom) Scroll(SB_VERT, item.Top + item.Height - client.bottom);
	}
	InvalidateRect(window_, nullptr, FALSE);
	if (notify && notify_) notify_(false);
}

void TVTCommentPanelSourceList::Paint(HDC target)
{
	RECT client;
	GetClientRect(window_, &client);
	if (client.right <= 0 || client.bottom <= 0) return;
	HDC buffer = CreateCompatibleDC(target);
	HBITMAP bitmap = CreateCompatibleBitmap(target, client.right, client.bottom);
	HDC dc = buffer && bitmap ? buffer : target;
	HGDIOBJ oldBitmap = dc == buffer ? SelectObject(dc, bitmap) : nullptr;
	int saved = SaveDC(dc);
	auto colors = theme_->GetListColors();
	Fill(dc, client, colors.Background);
	SetBkMode(dc, TRANSPARENT);
	for (size_t i = 0; i < layout_.size(); ++i) {
		RECT rect = ItemRect(static_cast<int>(i));
		if (rect.bottom <= 0 || rect.top >= client.bottom) continue;
		bool selected = selected_ == static_cast<int>(i);
		if (selected) Fill(dc, rect, colors.Selection);
		SetTextColor(dc, !IsWindowEnabled(window_) ? colors.DisabledText : selected ? colors.SelectionText : colors.Text);
		RECT text{ Px(6) - x_, rect.top + Px(4), max(client.right, static_cast<LONG>(width_)) - x_ - Px(6), rect.bottom - Px(4) };
		SelectObject(dc, boldFont_ ? boldFont_ : font_ ? font_ : GetStockObject(DEFAULT_GUI_FONT));
		DrawText(dc, items_[i].Name.c_str(), -1, &text, TEXT_FORMAT);
		text.top += layout_[i].NameHeight;
		SelectObject(dc, font_ ? font_ : GetStockObject(DEFAULT_GUI_FONT));
		DrawText(dc, items_[i].Details.c_str(), -1, &text, TEXT_FORMAT);
		if (i + 1 < items_.size()) {
			RECT line{ rect.left, rect.bottom - 1, rect.right, rect.bottom };
			Fill(dc, line, colors.Border);
		}
		if (selected && GetFocus() == window_) {
			rect.right = client.right;
			InflateRect(&rect, -1, -1);
			DrawFocusRect(dc, &rect);
		}
	}
	RestoreDC(dc, saved);
	if (dc == buffer) {
		BitBlt(target, 0, 0, client.right, client.bottom, dc, 0, 0, SRCCOPY);
		SelectObject(dc, oldBitmap);
	}
	if (bitmap) DeleteObject(bitmap);
	if (buffer) DeleteDC(buffer);
}

LRESULT CALLBACK TVTCommentPanelSourceList::WindowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
{
	if (message == WM_NCCREATE) {
		auto self = static_cast<TVTCommentPanelSourceList *>(reinterpret_cast<CREATESTRUCT *>(lParam)->lpCreateParams);
		self->window_ = hwnd;
		SetWindowLongPtr(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
	}
	auto self = reinterpret_cast<TVTCommentPanelSourceList *>(GetWindowLongPtr(hwnd, GWLP_USERDATA));
	if (self) return self->HandleMessage(message, wParam, lParam);
	return DefWindowProc(hwnd, message, wParam, lParam);
}

LRESULT TVTCommentPanelSourceList::HandleMessage(UINT message, WPARAM wParam, LPARAM lParam)
{
	switch (message) {
	case WM_PAINT: {
		PAINTSTRUCT paint;
		HDC dc = BeginPaint(window_, &paint);
		Paint(dc);
		EndPaint(window_, &paint);
		return 0;
	}
	case WM_PRINTCLIENT: Paint(reinterpret_cast<HDC>(wParam)); return 0;
	case WM_ERASEBKGND: return 1;
	case WM_SIZE: UpdateScrollbars(); return 0;
	case WM_SETFONT: {
		font_ = reinterpret_cast<HFONT>(wParam);
		if (boldFont_) DeleteObject(boldFont_);
		LOGFONT font{};
		GetObject(font_ ? font_ : GetStockObject(DEFAULT_GUI_FONT), sizeof(font), &font);
		font.lfWeight = FW_BOLD;
		boldFont_ = CreateFontIndirect(&font);
		Measure();
		return 0;
	}
	case WM_GETFONT: return reinterpret_cast<LRESULT>(font_);
	case WM_GETDLGCODE: return DLGC_WANTARROWS | DLGC_WANTCHARS;
	case WM_LBUTTONDOWN:
	case WM_LBUTTONDBLCLK:
		SetFocus(window_);
		Select(HitTest(GET_Y_LPARAM(lParam)), false, true);
		if (message == WM_LBUTTONDBLCLK && selected_ >= 0 && notify_) notify_(true);
		return 0;
	case WM_KEYDOWN:
		switch (wParam) {
		case VK_UP: Select(max(0, selected_ - 1), true, true); return 0;
		case VK_DOWN: Select(min(static_cast<int>(items_.size()) - 1, selected_ + 1), true, true); return 0;
		case VK_HOME: Select(0, true, true); Scroll(SB_VERT, 0); return 0;
		case VK_END: Select(static_cast<int>(items_.size()) - 1, true, true); Scroll(SB_VERT, height_); return 0;
		case VK_PRIOR: ScrollCommand(SB_VERT, SB_PAGEUP); return 0;
		case VK_NEXT: ScrollCommand(SB_VERT, SB_PAGEDOWN); return 0;
		case VK_LEFT: ScrollCommand(SB_HORZ, SB_LINEUP); return 0;
		case VK_RIGHT: ScrollCommand(SB_HORZ, SB_LINEDOWN); return 0;
		case VK_RETURN: if (selected_ >= 0 && notify_) notify_(true); return 0;
		}
		break;
	case WM_VSCROLL: ScrollCommand(SB_VERT, LOWORD(wParam)); return 0;
	case WM_HSCROLL: ScrollCommand(SB_HORZ, LOWORD(wParam)); return 0;
	case WM_MOUSEWHEEL: {
		UINT lines = 3;
		SystemParametersInfo(SPI_GETWHEELSCROLLLINES, 0, &lines, 0);
		RECT client;
		GetClientRect(window_, &client);
		wheel_ += GET_WHEEL_DELTA_WPARAM(wParam);
		int delta = wheel_ / WHEEL_DELTA;
		wheel_ %= WHEEL_DELTA;
		Scroll(SB_VERT, y_ - delta * (lines == WHEEL_PAGESCROLL ? client.bottom : static_cast<int>(lines) * lineHeight_));
		return 0;
	}
	case WM_MOUSEHWHEEL: Scroll(SB_HORZ, x_ + GET_WHEEL_DELTA_WPARAM(wParam) * lineHeight_ / WHEEL_DELTA); return 0;
	case WM_SETFOCUS:
	case WM_KILLFOCUS:
	case WM_ENABLE: InvalidateRect(window_, nullptr, FALSE); break;
	case LB_GETCOUNT: return items_.size();
	case LB_GETCURSEL: return selected_;
	case LB_SETCURSEL: Select(static_cast<int>(wParam), true, false); return selected_;
	case LB_GETTOPINDEX: return max(0, HitTest(0));
	case LB_GETTEXTLEN:
	case LB_GETTEXT:
	case LB_GETITEMRECT:
		if (wParam >= items_.size()) return LB_ERR;
		if (message == LB_GETITEMRECT) { *reinterpret_cast<RECT *>(lParam) = ItemRect(static_cast<int>(wParam)); return 0; }
		if (message == LB_GETTEXT) lstrcpy(reinterpret_cast<LPWSTR>(lParam), ItemText(static_cast<int>(wParam)).c_str());
		return ItemText(static_cast<int>(wParam)).size();
	case WM_NCDESTROY: {
		HWND window = window_;
		window_ = nullptr;
		font_ = nullptr;
		if (boldFont_) DeleteObject(boldFont_);
		boldFont_ = nullptr;
		SetWindowLongPtr(window, GWLP_USERDATA, 0);
		return DefWindowProc(window, message, wParam, lParam);
	}
	}
	return DefWindowProc(window_, message, wParam, lParam);
}
