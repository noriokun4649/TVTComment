#pragma once
#include <Windows.h>

class TVTCommentPanelDropDown
{
public:
	static void Attach(HWND combo);

private:
	static LRESULT CALLBACK ListSubclass(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR id, DWORD_PTR data);
	static void Fit(HWND combo, WINDOWPOS &position);
};
