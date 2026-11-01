#pragma once
#include "TVTCommentPanelTheme.h"
#include <functional>
#include <string>
#include <vector>

class TVTCommentPanelSourceList
{
public:
	struct Item { std::string Id; std::wstring Name, Details; };
	static LPCWSTR ClassName() { return L"TVTComment.Panel.SourceList"; }
	~TVTCommentPanelSourceList() { Finalize(); }
	HWND Create(HINSTANCE instance, HWND parent, int id, TVTCommentPanelTheme *theme, std::function<void(bool)> notify);
	void Finalize();
	void SetItems(std::vector<Item> items);
	void SetDPI(int dpi);
	std::string SelectedId() const;

private:
	struct Layout { int Top, Height, NameHeight; };
	static LRESULT CALLBACK WindowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam);
	LRESULT HandleMessage(UINT message, WPARAM wParam, LPARAM lParam);
	void Measure();
	void UpdateScrollbars();
	void Scroll(int bar, int position);
	void ScrollCommand(int bar, UINT command);
	void Select(int index, bool ensureVisible, bool notify);
	int HitTest(int y) const;
	RECT ItemRect(int index) const;
	std::wstring ItemText(int index) const;
	void Paint(HDC dc);
	int Px(int value) const { return MulDiv(value, dpi_, 96); }

	HWND window_ = nullptr;
	HINSTANCE instance_ = nullptr;
	HFONT font_ = nullptr, boldFont_ = nullptr;
	TVTCommentPanelTheme *theme_ = nullptr;
	std::function<void(bool)> notify_;
	std::vector<Item> items_;
	std::vector<Layout> layout_;
	int dpi_ = 96, lineHeight_ = 16, width_ = 0, height_ = 0, x_ = 0, y_ = 0, selected_ = -1, wheel_ = 0;
	bool updatingScroll_ = false;
};
