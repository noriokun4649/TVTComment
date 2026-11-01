#pragma once
#include "NicoJK/TVTestPlugin.h"
#include <commctrl.h>

class TVTCommentPanelTheme
{
public:
	struct ListColors { COLORREF Background, Text, Selection, SelectionText, Border, DisabledText; };
	~TVTCommentPanelTheme();
	void Initialize(TVTest::CTVTestApp *app, TVTest::PluginParam *parameter);
	void Update(HWND window, int dpi);
	bool HandleMessage(HWND window, UINT message, WPARAM wParam, LPARAM lParam, LRESULT &result);
	ListColors GetListColors() const { return { input_, inputText_, selection_, selectionText_, border_, disabled_ }; }

private:
	static LRESULT CALLBACK ControlSubclass(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR id, DWORD_PTR data);
	void ApplyControl(HWND control);
	void PaintControl(HWND control, HDC dc);
	void PaintControlBuffered(HWND control, HDC dc);
	void PaintBorder(HWND control, HDC dc);
	void DrawComboItem(const DRAWITEMSTRUCT &item);
	void DrawRow(HWND list, HDC dc, int row, bool selected);
	void Background(HDC dc, const RECT &rect, LPCWSTR style, COLORREF fallback);
	void Label(HDC dc, RECT rect, LPCWSTR text, COLORREF color, UINT format = DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS | DT_NOPREFIX);
	COLORREF Color(LPCWSTR name, COLORREF fallback) const;
	int Px(int value) const { return MulDiv(value, dpi_, 96); }

	TVTest::CTVTestApp *app_ = nullptr;
	TVTest::PluginParam *parameter_ = nullptr;
	HBRUSH backgroundBrush_ = nullptr, inputBrush_ = nullptr;
	COLORREF background_ = 0, text_ = 0, input_ = 0, inputText_ = 0, border_ = 0, selection_ = 0, selectionText_ = 0, disabled_ = 0;
	int dpi_ = 96;
	bool dark_ = false, updating_ = false;
};
