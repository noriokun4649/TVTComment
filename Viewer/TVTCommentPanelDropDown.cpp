#include "stdafx.h"
#include "TVTCommentPanelDropDown.h"
#include <commctrl.h>

void TVTCommentPanelDropDown::Attach(HWND combo)
{
	COMBOBOXINFO info{ sizeof(info) };
	if (GetComboBoxInfo(combo, &info))
		SetWindowSubclass(info.hwndList, ListSubclass, 1, reinterpret_cast<DWORD_PTR>(combo));
}

void TVTCommentPanelDropDown::Fit(HWND combo, WINDOWPOS &position)
{
	RECT control, popup, panel;
	MONITORINFO monitor{ sizeof(monitor) };
	if (!GetWindowRect(combo, &control) || !GetWindowRect(position.hwnd, &popup)
		|| !GetMonitorInfo(MonitorFromWindow(combo, MONITOR_DEFAULTTONEAREST), &monitor)) return;
	RECT bounds = monitor.rcWork;
	GetClientRect(GetParent(combo), &panel);
	MapWindowPoints(GetParent(combo), nullptr, reinterpret_cast<POINT *>(&panel), 2);
	RECT visible;
	if (IntersectRect(&visible, &bounds, &panel)) bounds = visible;
	int width = position.flags & SWP_NOSIZE ? popup.right - popup.left : position.cx;
	int height = position.flags & SWP_NOSIZE ? popup.bottom - popup.top : position.cy;
	int above = max(0L, min(bounds.bottom, control.top) - bounds.top);
	int below = max(0L, bounds.bottom - max(bounds.top, control.bottom));
	bool upward = height > below && above > below;
	width = max(1, min(width, static_cast<int>(bounds.right - bounds.left)));
	height = max(1, min(height, upward ? above : below));
	position.x = max(bounds.left, min(control.left, bounds.right - width));
	position.y = max(bounds.top, min(upward ? control.top - height : control.bottom, bounds.bottom - height));
	position.cx = width;
	position.cy = height;
	position.flags &= ~(SWP_NOMOVE | SWP_NOSIZE);
}

LRESULT CALLBACK TVTCommentPanelDropDown::ListSubclass(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR id, DWORD_PTR data)
{
	LRESULT result = DefSubclassProc(hwnd, message, wParam, lParam);
	if (message == WM_WINDOWPOSCHANGING) {
		auto position = reinterpret_cast<WINDOWPOS *>(lParam);
		if (!(position->flags & SWP_HIDEWINDOW) && ((position->flags & SWP_SHOWWINDOW) || IsWindowVisible(hwnd)))
			Fit(reinterpret_cast<HWND>(data), *position);
	}
	if (message == WM_NCDESTROY) RemoveWindowSubclass(hwnd, ListSubclass, id);
	return result;
}
