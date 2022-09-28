#pragma once

#include "TVTComment/TVTComment.h"
#include <atomic>
#include <thread>

// プラグインクラス
class CViewer : public TVTest::CTVTestPlugin
{
public:
	// チャンネル変更などの後に適当な実況IDのチェックを行うまでの猶予
	static const int SETUP_CURJK_DELAY = 3000;
	// CTVTestPlugin
	CViewer();
	bool GetPluginInfo(TVTest::PluginInfo *pInfo);
	bool Initialize();
	bool Finalize();
private:
	struct SETTINGS {
		int hideForceWindow;
		int timerInterval;
		int halfSkipThreshold;
		int commentLineMargin;
		int commentFontOutline;
		int commentSize;
		int commentSizeMin;
		int commentSizeMax;
		TCHAR commentFontName[LF_FACESIZE];
		TCHAR commentFontNameMulti[LF_FACESIZE];
		TCHAR commentFontNameEmoji[LF_FACESIZE];
		bool bCommentFontBold;
		bool bCommentFontAntiAlias;
		int commentDuration;
		int commentDrawLineCount;
		bool bUseOsdCompositor;
		bool bUseTexture;
		bool bUseDrawingThread;
		int defaultPlaybackDelay;
		int forwardList[26];
		int commentOpacity;
		tstring tvtCommentPath;
		bool hideCommentHighlight;
	};
	bool TogglePlugin(bool bEnabled);
	void ToggleStreamCallback(bool bSet);
	void SyncThread();
	void LoadFromIni();
	HWND GetFullscreenWindow();
	HWND FindVideoContainer();
	LONGLONG GetCurrentTot();
	static LRESULT CALLBACK EventCallback(UINT Event, LPARAM lParam1, LPARAM lParam2, void *pClientData);
	static BOOL CALLBACK WindowMsgCallback(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam, LRESULT *pResult, void *pUserData);
	static LRESULT CALLBACK ForceWindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam);
	LRESULT ForceWindowProcMain(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam);
	static BOOL CALLBACK StreamCallback(BYTE *pData, void *pClientData);

	// 設定ファイルの名前
	tstring iniFileName_;
	SETTINGS s_;

	// タイマーとかを管理するためのダミー窓
	HWND hDummy_;

	// コメント描画ウィンドウ
	CCommentWindow commentWindow_;
	DWORD forwardTick_;
	std::thread syncThread_;
	std::atomic_bool bQuitSyncThread_;
	std::atomic_bool bPendingTimerForward_;
	std::atomic_bool bHalfSkip_;
	bool bFlipFlop_;
	LONGLONG forwardOffset_;
	LONGLONG forwardOffsetDelta_;

	// 過去ログ関係
	bool bSetStreamCallback_;
	bool bResyncComment_;
	LONGLONG llftTot_;
	LONGLONG llftTotLast_;
	LONGLONG llftTotPending_;
	DWORD totTick_;
	DWORD totTickLast_;
	DWORD totTickPending_;
	DWORD totPcr_;
	DWORD pcr_;
	DWORD pcrTick_;
	int pcrPid_;
	int pcrPids_[8];
	int pcrPidCounts_[8];
	recursive_mutex_ streamLock_;

#pragma region TVTComment
	std::unique_ptr<TVTComment::TVTComment> tvtComment;
#pragma endregion
};
