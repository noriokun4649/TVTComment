/*
	NicoJK
		TVTest ニコニコ実況プラグイン
*/

#include "stdafx.h"
#include "NicoJK/Util.h"
#include "NicoJK/CommentWindow.h"
#define TVTEST_PLUGIN_CLASS_IMPLEMENT
#include "NicoJK/TVTestPlugin.h"
#include "resource.h"
#include "Viewer.h"
#include <dwmapi.h>
#include <shellapi.h>

#pragma comment(lib, "dwmapi.lib")

#ifdef _DEBUG
#include <stdarg.h>
inline void dprintf_real( const _TCHAR * fmt, ... )
{
  _TCHAR buf[1024];
  va_list ap;
  va_start(ap, fmt);
  _vsntprintf_s(buf, 1024, fmt, ap);
  va_end(ap);
  OutputDebugString(buf);
}
#  define dprintf dprintf_real
#else
#  define dprintf __noop
#endif

#define WM_RESET_STREAM (WM_APP + 105)

enum {
	TIMER_FORWARD = 1,
	TIMER_SETUP_CURJK,
	TIMER_DONE_MOVE,
	TIMER_DONE_SIZE,
};

CViewer::CViewer()
	: hDummy_(nullptr)
	, forwardTick_(0)
	, bQuitSyncThread_(false)
	, bPendingTimerForward_(false)
	, bHalfSkip_(false)
	, bFlipFlop_(false)
	, forwardOffset_(0)
	, forwardOffsetDelta_(0)
	, bSetStreamCallback_(false)
	, bResyncComment_(false)
	, llftTot_(-1)
	, pcr_(0)
	, pcrTick_(0)
	, pcrPid_(-1)
{
	SETTINGS s = {};
	s_ = s;
	pcrPids_[0] = -1;
}

bool CViewer::GetPluginInfo(TVTest::PluginInfo *pInfo)
{
	// プラグインの情報を返す
	pInfo->Type           = TVTest::PLUGIN_TYPE_NORMAL;
	pInfo->Flags          = 0;
	pInfo->pszPluginName  = L"TvtComment";
	pInfo->pszCopyright   = L"(c) 2017 silane / 2022 noriokun4649";
	pInfo->pszDescription = L"実況コメントをオーバーレイ表示";
	return true;
}

bool CViewer::Initialize()
{
	// ウィンドウクラスを登録
	WNDCLASSEX wc = {};
	wc.cbSize = sizeof(wc);
	wc.style = 0;
	wc.lpfnWndProc = ForceWindowProc;
	wc.hInstance = g_hinstDLL;
	wc.lpszClassName = TEXT("TvtComment");
	if (RegisterClassEx(&wc) == 0) {
		return false;
	}
	// 初期化処理
	TCHAR path[MAX_PATH];
	iniFileName_.clear();
	if (GetLongModuleFileName(g_hinstDLL, path, _countof(path))) {
		iniFileName_ = path;
		size_t lastSep = iniFileName_.find_last_of(TEXT("/\\."));
		if (lastSep != tstring::npos && iniFileName_[lastSep] == TEXT('.')) {
			iniFileName_.erase(lastSep);
		}
		iniFileName_ += TEXT(".ini");
	}
	// OsdCompositorは他プラグインと共用することがあるので、有効にするならFinalize()まで破棄しない
	bool bEnableOsdCompositor = GetPrivateProfileInt(TEXT("Setting"), TEXT("enableOsdCompositor"), 0, iniFileName_.c_str()) != 0;
	// フィルタグラフを取得できないバージョンではAPIフックを使う
	bool bSetHookOsdCompositor = m_pApp->GetVersion() < TVTest::MakeVersion(0, 9, 0);
	if (!commentWindow_.Initialize(g_hinstDLL, &bEnableOsdCompositor, bSetHookOsdCompositor)) {
		return false;
	}
	if (bEnableOsdCompositor) {
		m_pApp->AddLog(L"OsdCompositorを初期化しました。");
	}
	 int dpi = m_pApp->GetDPIFromWindow(m_pApp->GetAppWindow()); 
	 int iconWidth = 16 * dpi / 96; 
	 int iconHeight = 16 * dpi / 96; 
	 m_pApp->GetStyleValuePixels(L"side-bar.item.icon.width", dpi, &iconWidth); 
	 m_pApp->GetStyleValuePixels(L"side-bar.item.icon.height", dpi, &iconHeight); 
	 bool bSmallIcon = iconWidth <= 16 && iconHeight <= 16; 
	// TvtCommentメインアイコンを登録 
	m_pApp->RegisterPluginIconFromResource(g_hinstDLL, MAKEINTRESOURCE(IDB_TVTCICON));

	// 最前面コマンドを登録
	TVTest::PluginCommandInfo ci;
	ci.Size = sizeof(ci);
	ci.Flags = 0;
	ci.Flags = TVTest::PLUGIN_COMMAND_FLAG_ICONIZE; 
	ci.State = TVTest::PLUGIN_COMMAND_STATE_DISABLED;

	ci.ID = static_cast<int>(TVTComment::Command::ShowWindow);
	ci.pszText = L"ShowWindow";
	ci.pszDescription = ci.pszName = L"TvtCommentウィンドウの前面表示";
	ci.hbmIcon = static_cast<HBITMAP>(LoadImage(g_hinstDLL, MAKEINTRESOURCE(bSmallIcon ? IDB_TVTCTOPICON16 : IDB_TVTCTOPICON), IMAGE_BITMAP, 0, 0, LR_CREATEDIBSECTION));
	if (!m_pApp->RegisterPluginCommand(&ci)) {
		m_pApp->RegisterCommand(ci.ID, ci.pszText, ci.pszName);
	}
	DeleteObject(ci.hbmIcon);

	//非表示コマンドを登録
	ci.ID = static_cast<int>(TVTComment::Command::HideComment);
	ci.pszText = L"HideComment";
	ci.pszDescription = ci.pszName = L"TvtCommentコメントの非表示";
	ci.hbmIcon = static_cast<HBITMAP>(LoadImage(g_hinstDLL, MAKEINTRESOURCE(bSmallIcon ? IDB_TVTCVIEWICON16 : IDB_TVTCVIEWICON), IMAGE_BITMAP, 0, 0, LR_CREATEDIBSECTION));
	if (!m_pApp->RegisterPluginCommand(&ci)) {
		m_pApp->RegisterCommand(ci.ID, ci.pszText, ci.pszName);
	}
	DeleteObject(ci.hbmIcon);

	// イベントコールバック関数を登録
	m_pApp->SetEventCallback(EventCallback, this);
	return true;
}

bool CViewer::Finalize()
{
	// 終了処理
	TogglePlugin(false);
	commentWindow_.Finalize();
	return true;
}

bool CViewer::TogglePlugin(bool bEnabled)
{
	if (bEnabled) {
		if (!hDummy_) {
			LoadFromIni();

#pragma region TVTComment
			//tvtCommentオブジェクトは勢いウィンドウより生存期間が長い
			// tstringをwstringに渡してるけどどうせUnicodeでしかビルドしないのでヨシ！
			tvtComment = std::make_unique<TVTComment::TVTComment>(m_pApp, &commentWindow_, s_.tvtCommentPath);
#pragma endregion

			// ダミー窓作成
			hDummy_ = CreateWindowEx(0, TEXT("TvtComment"), nullptr, WS_POPUP, 0, 0, 0, 0, nullptr, nullptr, g_hinstDLL, this);
			if (hDummy_) {
				// ウィンドウコールバック関数を登録
				m_pApp->SetWindowMessageCallback(WindowMsgCallback, this);
				// ストリームコールバック関数を登録(指定ファイル再生機能のために常に登録)
				ToggleStreamCallback(true);
				// DWMの更新タイミングでTIMER_FORWARDを呼ぶスレッドを開始(Vista以降)
				if (s_.timerInterval < 0) {
					OSVERSIONINFOEX vi;
					vi.dwOSVersionInfoSize = sizeof(vi);
					vi.dwMajorVersion = 6;
					BOOL bCompEnabled;
					// ここで"dwmapi.dll"を遅延読み込みしていることに注意(つまりXPではDwm*()を踏んではいけない)
					if (VerifyVersionInfo(&vi, VER_MAJORVERSION, VerSetConditionMask(0, VER_MAJORVERSION, VER_GREATER_EQUAL)) &&
					    SUCCEEDED(DwmIsCompositionEnabled(&bCompEnabled)) && bCompEnabled) {
						bQuitSyncThread_ = false;
						syncThread_ = std::thread([this]() { SyncThread(); });
						SetThreadPriority(syncThread_.native_handle(), THREAD_PRIORITY_ABOVE_NORMAL);
					}
					if (!syncThread_.joinable()) {
						m_pApp->AddLog(L"Aeroが無効のため設定timerIntervalのリフレッシュ同期機能はオフになります。");
						SetTimer(hDummy_, TIMER_FORWARD, 166667 / -s_.timerInterval, nullptr);
					}
				}
			}
		}
		return hDummy_ != nullptr;
	} else {
		if (hDummy_) {
			DestroyWindow(hDummy_);
		}
#pragma region TVTComment
		tvtComment = nullptr;
#pragma endregion
		return true;
	}
}

void CViewer::SyncThread()
{
	DWORD count = 0;
	int timeout = 0;
	while (!bQuitSyncThread_) {
		if (FAILED(DwmFlush())) {
			// ビジーに陥らないように
			Sleep(500);
		}
		if (count >= 10000) {
			// 捌き切れない量のメッセージを送らない
			if (bPendingTimerForward_ && --timeout >= 0) {
				continue;
			}
			count -= 10000;
			timeout = 30;
			bPendingTimerForward_ = true;
			SendNotifyMessage(hDummy_, WM_TIMER, TIMER_FORWARD, 0);
		}
		count += bHalfSkip_ ? -s_.timerInterval / 2 : -s_.timerInterval;
	}
}

void CViewer::ToggleStreamCallback(bool bSet)
{
	if (bSet) {
		if (!bSetStreamCallback_) {
			bSetStreamCallback_ = true;
			pcrPid_ = -1;
			pcrPids_[0] = -1;
			m_pApp->SetStreamCallback(0, StreamCallback, this);
		}
	} else {
		if (bSetStreamCallback_) {
			m_pApp->SetStreamCallback(TVTest::STREAM_CALLBACK_REMOVE, StreamCallback);
			bSetStreamCallback_ = false;
		}
	}
}

void CViewer::LoadFromIni()
{
	// iniはセクション単位で読むと非常に速い。起動時は処理が混み合うのでとくに有利
	std::vector<TCHAR> buf = GetPrivateProfileSectionBuffer(TEXT("Setting"), iniFileName_.c_str());
	s_.timerInterval		= GetBufferedProfileInt(buf.data(), TEXT("timerInterval"), -10000);
	s_.halfSkipThreshold	= GetBufferedProfileInt(buf.data(), TEXT("halfSkipThreshold"), 9999);
	s_.commentLineMargin	= GetBufferedProfileInt(buf.data(), TEXT("commentLineMargin"), 125);
	s_.commentFontOutline	= GetBufferedProfileInt(buf.data(), TEXT("commentFontOutline"), 0);
	s_.commentSize			= GetBufferedProfileInt(buf.data(), TEXT("commentSize"), 100);
	s_.commentSizeMin		= GetBufferedProfileInt(buf.data(), TEXT("commentSizeMin"), 16);
	s_.commentSizeMax		= GetBufferedProfileInt(buf.data(), TEXT("commentSizeMax"), 9999);
	GetBufferedProfileString(buf.data(), TEXT("commentFontName"), TEXT("メイリオ"), s_.commentFontName, _countof(s_.commentFontName));
	GetBufferedProfileString(buf.data(), TEXT("commentFontNameMulti"), TEXT("メイリオ"), s_.commentFontNameMulti, _countof(s_.commentFontNameMulti));
	GetBufferedProfileString(buf.data(), TEXT("commentFontNameEmoji"), TEXT(""), s_.commentFontNameEmoji, _countof(s_.commentFontNameEmoji));
	s_.bCommentFontBold		= GetBufferedProfileInt(buf.data(), TEXT("commentFontBold"), 1) != 0;
	s_.bCommentFontAntiAlias = GetBufferedProfileInt(buf.data(), TEXT("commentFontAntiAlias"), 1) != 0;
	s_.commentDuration		= GetBufferedProfileInt(buf.data(), TEXT("commentDuration"), CCommentWindow::DISPLAY_DURATION);
	s_.commentDrawLineCount = GetBufferedProfileInt(buf.data(), TEXT("commentDrawLineCount"), CCommentWindow::DEFAULT_LINE_DRAW_COUNT);
	s_.bUseOsdCompositor	= GetBufferedProfileInt(buf.data(), TEXT("useOsdCompositor"), 0) != 0;
	s_.bUseTexture			= GetBufferedProfileInt(buf.data(), TEXT("useTexture"), 1) != 0;
	s_.bUseDrawingThread	= GetBufferedProfileInt(buf.data(), TEXT("useDrawingThread"), 1) != 0;
	s_.defaultPlaybackDelay	= GetBufferedProfileInt(buf.data(), TEXT("defaultPlaybackDelay"), 500);

#pragma region TVTComment
	TCHAR path[MAX_PATH];
	GetPrivateProfileString(TEXT("TvtComment"), TEXT("ExePath"), TEXT("TvtComment/TvtComment.exe"), path, _countof(path), iniFileName_.c_str());
	if (path[0] && !_tcschr(TEXT("/\\"), path[0]) && path[1] != TEXT(':')) {
		// 相対パス
		TCHAR dir[MAX_PATH];
		if (GetLongModuleFileName(g_hinstDLL, dir, _countof(dir))) {
			s_.tvtCommentPath = dir;
			size_t lastSep = s_.tvtCommentPath.find_last_of(TEXT("/\\"));
			if (lastSep != tstring::npos) {
				s_.tvtCommentPath.erase(lastSep + 1);
			}
			s_.tvtCommentPath += path;
		} else {
			s_.tvtCommentPath.clear();
		}
	} else {
		s_.tvtCommentPath = path;
	}
#pragma endregion
}

HWND CViewer::GetFullscreenWindow()
{
	TVTest::HostInfo hostInfo;
	if (m_pApp->GetFullscreen() && m_pApp->GetHostInfo(&hostInfo)) {
		TCHAR className[64];
		_tcsncpy_s(className, hostInfo.pszAppName, 47);
		_tcscat_s(className, TEXT(" Fullscreen"));

		HWND hwnd = nullptr;
		while ((hwnd = FindWindowEx(nullptr, hwnd, className, nullptr)) != nullptr) {
			DWORD pid;
			GetWindowThreadProcessId(hwnd, &pid);
			if (pid == GetCurrentProcessId()) {
				return hwnd;
			}
		}
	}
	return nullptr;
}

static BOOL CALLBACK EnumWindowsProc(HWND hwnd, LPARAM lParam)
{
	std::pair<HWND, LPCTSTR> *params = reinterpret_cast<std::pair<HWND, LPCTSTR>*>(lParam);
	TCHAR className[64];
	if (GetClassName(hwnd, className, _countof(className)) && !_tcscmp(className, params->second)) {
		// 見つかった
		params->first = hwnd;
		return FALSE;
	}
	return TRUE;
}

// TVTestのVideo Containerウィンドウを探す
HWND CViewer::FindVideoContainer()
{
	std::pair<HWND, LPCTSTR> params(nullptr, nullptr);
	TVTest::HostInfo hostInfo;
	if (m_pApp->GetHostInfo(&hostInfo)) {
		TCHAR searchName[64];
		_tcsncpy_s(searchName, hostInfo.pszAppName, 31);
		_tcscat_s(searchName, TEXT(" Video Container"));

		params.second = searchName;
		HWND hwndFull = GetFullscreenWindow();
		EnumChildWindows(hwndFull ? hwndFull : m_pApp->GetAppWindow(), EnumWindowsProc, reinterpret_cast<LPARAM>(&params));
	}
	return params.first;
}

// 再生中のストリームのTOT時刻(取得からの経過時間で補正済み)をUTCで取得する
LONGLONG CViewer::GetCurrentTot()
{
	lock_recursive_mutex lock(streamLock_);
	DWORD tick = GetTickCount();
	if (llftTot_ < 0) {
		// TOTを取得できていない
		return -1;
	} else if (tick - pcrTick_ >= 2000) {
		// 2秒以上PCRを取得できていない→ポーズ中?
		return llftTot_ - s_.defaultPlaybackDelay * FILETIME_MILLISECOND;
	} else if (llftTotLast_ < 0) {
		// 再生速度は分からない
		return llftTot_ + (static_cast<int>(tick - totTick_) - s_.defaultPlaybackDelay) * FILETIME_MILLISECOND;
	} else {
		DWORD delta = totTick_ - totTickLast_;
		// 再生速度(10%～1000%)
		LONGLONG speed = !delta ? FILETIME_MILLISECOND : (llftTot_ - llftTotLast_) / delta;
		speed = min(max(speed, FILETIME_MILLISECOND / 10), FILETIME_MILLISECOND * 10);
		return llftTot_ + (static_cast<int>(tick - totTick_) - s_.defaultPlaybackDelay) * speed;
	}
}

static inline int CounterDiff(DWORD a, DWORD b)
{
	return (a - b) & 0x80000000 ? -static_cast<int>(b - a - 1) - 1 : static_cast<int>(a - b);
}

static int GetWindowHeight(HWND hwnd)
{
	RECT rc;
	return hwnd && GetWindowRect(hwnd, &rc) ? rc.bottom - rc.top : 0;
}

// イベントコールバック関数
// 何かイベントが起きると呼ばれる
LRESULT CALLBACK CViewer::EventCallback(UINT Event, LPARAM lParam1, LPARAM lParam2, void *pClientData)
{
	static_cast<void>(lParam2);
	CViewer *pThis = static_cast<CViewer*>(pClientData);
	switch (Event) {
	case TVTest::EVENT_PLUGINENABLE:
		// プラグインの有効状態が変化した
		pThis->TogglePlugin(lParam1 != 0);
		return TRUE;
	case TVTest::EVENT_FULLSCREENCHANGE:
		// 全画面表示状態が変化した
		if (pThis->hDummy_) {
			// オーナーが変わるのでコメントウィンドウを作りなおす
			pThis->commentWindow_.Destroy();
			if (pThis->commentWindow_.GetOpacity() != 0 && pThis->m_pApp->GetPreview()) {
				HWND hwnd = pThis->FindVideoContainer();
				pThis->commentWindow_.Create(hwnd);
				pThis->bHalfSkip_ = GetWindowHeight(hwnd) >= pThis->s_.halfSkipThreshold;
			}
		}
		break;
	case TVTest::EVENT_PREVIEWCHANGE:
		// プレビュー表示状態が変化した
		if (pThis->hDummy_) {
			if (pThis->commentWindow_.GetOpacity() != 0 && lParam1 != 0) {
				HWND hwnd = pThis->FindVideoContainer();
				pThis->commentWindow_.Create(hwnd);
				pThis->bHalfSkip_ = GetWindowHeight(hwnd) >= pThis->s_.halfSkipThreshold;
			} else {
				pThis->commentWindow_.Destroy();
			}
		}
		break;
	case TVTest::EVENT_DRIVERCHANGE:
	case TVTest::EVENT_CHANNELCHANGE:
		// チャンネルが変更された
		if (pThis->hDummy_) {
			PostMessage(pThis->hDummy_, WM_RESET_STREAM, 0, 0);
		}
		// FALL THROUGH!
	case TVTest::EVENT_SERVICECHANGE:
		// サービスが変更された
		if (pThis->hDummy_) {
			// 重複やザッピング対策のためタイマで呼ぶ
			SetTimer(pThis->hDummy_, TIMER_SETUP_CURJK, SETUP_CURJK_DELAY, nullptr);
		}
		break;
	case TVTest::EVENT_SERVICEUPDATE:
		// サービスの構成が変化した(再生ファイルを切り替えたときなど)
		if (pThis->hDummy_) {
			// ユーザの自発的なチャンネル変更(EVENT_CHANNELCHANGE)を捉えるのが原則だが
			// 非チューナ系のBonDriverだとこれでは不十分なため
			SetTimer(pThis->hDummy_, TIMER_SETUP_CURJK, SETUP_CURJK_DELAY, nullptr);
		}
		break;
	case TVTest::EVENT_COMMAND:
		// コマンドが選択された
		if (pThis->hDummy_) {
			pThis->tvtComment->OnCommandInvoked(static_cast<TVTComment::Command>(lParam1));
		}
		break;
	case TVTest::EVENT_FILTERGRAPH_INITIALIZED:
		// フィルタグラフの初期化終了
		pThis->commentWindow_.OnFilterGraphInitialized(reinterpret_cast<const TVTest::FilterGraphInfo*>(lParam1)->pGraphBuilder);
		break;
	case TVTest::EVENT_FILTERGRAPH_FINALIZE:
		// フィルタグラフの終了処理開始
		pThis->commentWindow_.OnFilterGraphFinalize(reinterpret_cast<const TVTest::FilterGraphInfo*>(lParam1)->pGraphBuilder);
		break;
	}
	return 0;
}

BOOL CALLBACK CViewer::WindowMsgCallback(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam, LRESULT *pResult, void *pUserData)
{
	static_cast<void>(hwnd);
	static_cast<void>(wParam);
	static_cast<void>(lParam);
	static_cast<void>(pResult);
	CViewer *pThis = static_cast<CViewer*>(pUserData);
	switch (uMsg) {
	case WM_MOVE:
		pThis->commentWindow_.OnParentMove();
		// 実際に捉えたいVideo Containerウィンドウの変化はすこし遅れるため
		SetTimer(pThis->hDummy_, TIMER_DONE_MOVE, 500, nullptr);
		break;
	case WM_SIZE:
		pThis->commentWindow_.OnParentSize();
		SetTimer(pThis->hDummy_, TIMER_DONE_SIZE, 500, nullptr);
		break;
	}
	return FALSE;
}

LRESULT CALLBACK CViewer::ForceWindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam)
{
	if (uMsg == WM_CREATE) {
		SetWindowLongPtr(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(reinterpret_cast<LPCREATESTRUCT>(lParam)->lpCreateParams));
	}
	CViewer *pThis = reinterpret_cast<CViewer*>(GetWindowLongPtr(hwnd, GWLP_USERDATA));
	return pThis ? pThis->ForceWindowProcMain(hwnd, uMsg, wParam, lParam) : DefWindowProc(hwnd, uMsg, wParam, lParam);
}

LRESULT CViewer::ForceWindowProcMain(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam)
{
#pragma region TVTComment
	if (tvtComment->WindowProc(hwnd, uMsg, wParam, lParam))
		return TRUE;
#pragma endregion

	switch (uMsg) {
	case WM_CREATE:
		commentWindow_.SetStyle(s_.commentFontName, s_.commentFontNameMulti, s_.commentFontNameEmoji, s_.bCommentFontBold, s_.bCommentFontAntiAlias,
		                        s_.commentFontOutline, s_.bUseOsdCompositor, s_.bUseTexture, s_.bUseDrawingThread);
		commentWindow_.SetCommentSize(s_.commentSize, s_.commentSizeMin, s_.commentSizeMax, s_.commentLineMargin);
		commentWindow_.SetDisplayDuration(s_.commentDuration);
		commentWindow_.SetDrawLineCount(s_.commentDrawLineCount);
		commentWindow_.SetOpacity(static_cast<BYTE>(s_.commentOpacity));
		if (commentWindow_.GetOpacity() != 0 && m_pApp->GetPreview()) {
			HWND hwndContainer = FindVideoContainer();
			commentWindow_.Create(hwndContainer);
			bHalfSkip_ = GetWindowHeight(hwndContainer) >= s_.halfSkipThreshold;
		}
		forwardTick_ = timeGetTime();
		forwardOffset_ = 0;
		forwardOffsetDelta_ = 0;
		SendMessage(hwnd, WM_RESET_STREAM, 0, 0);

		if (s_.timerInterval >= 0) {
			SetTimer(hwnd, TIMER_FORWARD, s_.timerInterval, nullptr);
		}
		SetTimer(hwnd, TIMER_SETUP_CURJK, SETUP_CURJK_DELAY, nullptr);

		// TVTest起動直後はVideo Containerウィンドウの配置が定まっていないようなので再度整える
		SetTimer(hwnd, TIMER_DONE_SIZE, 500, nullptr);
		m_pApp->SetPluginCommandState(static_cast<int>(TVTComment::Command::ShowWindow), 0);
		m_pApp->SetPluginCommandState(static_cast<int>(TVTComment::Command::HideComment), 0);
		return TRUE;
	case WM_DESTROY:
		commentWindow_.Destroy();

		if (syncThread_.joinable()) {
			bQuitSyncThread_ = true;
			syncThread_.join();
		}
		ToggleStreamCallback(false);
		m_pApp->SetWindowMessageCallback(nullptr);
		m_pApp->SetPluginCommandState(static_cast<int>(TVTComment::Command::ShowWindow), TVTest::PLUGIN_COMMAND_STATE_DISABLED);
		m_pApp->SetPluginCommandState(static_cast<int>(TVTComment::Command::HideComment), TVTest::PLUGIN_COMMAND_STATE_DISABLED);
		hDummy_ = nullptr;
		break;
#pragma region TVTComment
	case TVTComment::TVTComment::WM_SETCHATOPACITY:
	{
		BYTE newOpacity = (BYTE)wParam;
		if (commentWindow_.GetOpacity() == 0 && newOpacity != 0 && m_pApp->GetPreview()) {
			commentWindow_.ClearChat();
			HWND hwndContainer = FindVideoContainer();
			commentWindow_.Create(hwndContainer);
			bHalfSkip_ = GetWindowHeight(hwndContainer) >= s_.halfSkipThreshold;
		}
		else if (commentWindow_.GetOpacity() != 0 && newOpacity == 0) {
			commentWindow_.Destroy();
		}
		commentWindow_.SetOpacity(newOpacity);
		break;
	}
#pragma endregion
	case WM_TIMER:
		switch (wParam) {
		case TIMER_FORWARD:
			bFlipFlop_ = !bFlipFlop_;
			if (syncThread_.joinable() || !bHalfSkip_ || bFlipFlop_) {
				bool resyncComment = false;
				{
					lock_recursive_mutex lock(streamLock_);
					if (bResyncComment_) {
						resyncComment = true;
						bResyncComment_ = false;
					}
				}
				// オフセットを調整する
				bool bNotify = false;
				if (0 < forwardOffsetDelta_ && forwardOffsetDelta_ <= 30000) {
					// 前進させて調整
					int delta = min(static_cast<int>(forwardOffsetDelta_), forwardOffsetDelta_ < 10000 ? 500 : 2000);
					forwardOffset_ += delta;
					forwardOffsetDelta_ -= delta;
					bNotify = forwardOffsetDelta_ == 0;
					commentWindow_.Forward(delta);
				} else if (forwardOffsetDelta_ != 0) {
					// ログファイルを閉じて一気に調整
					forwardOffset_ += forwardOffsetDelta_;
					forwardOffsetDelta_ = 0;
					bNotify = true;
					commentWindow_.ClearChat();
				} else if (resyncComment) {
					// シーク時のコメント再生位置の再調整
					commentWindow_.ClearChat();
				}
				if (bNotify) {
					TCHAR text[64];
					int sign = forwardOffset_ < 0 ? -1 : 1;
					LONGLONG absSec = sign * forwardOffset_ / 1000;
					LONGLONG absMin = absSec / 60;
					LONGLONG absHour = absMin / 60;
					if (absSec < 60) {
						_stprintf_s(text, TEXT("(Offset %lld)"), sign * absSec);
					} else if (absMin < 60) {
						_stprintf_s(text, TEXT("(Offset %lld:%02lld)"), sign * absMin, absSec % 60);
					} else if (absHour < 24) {
						_stprintf_s(text, TEXT("(Offset %lld:%02lld:%02lld)"), sign * absHour, absMin % 60, absSec % 60);
					} else {
						_stprintf_s(text, TEXT("(Offset %lld'%02lld:%02lld:%02lld)"), sign * (absHour / 24), absHour % 24, absMin % 60, absSec % 60);
					}
					commentWindow_.AddChat(text, RGB(0x00,0xFF,0xFF), CCommentWindow::CHAT_POS_UE);
				}
				// コメントの表示を進める
				DWORD tick = timeGetTime();
				commentWindow_.Forward(min(static_cast<int>(tick - forwardTick_), 5000));
				forwardTick_ = tick;
				// 過去ログがあれば処理する
				LONGLONG llft = GetCurrentTot();
				if (llft >= 0) {
					LONGLONG tm = FileTimeToUnixTime(llft);
					tm = min(max(forwardOffset_ < 0 ? tm - (-forwardOffset_ / 1000) : tm + forwardOffset_ / 1000, 0LL), UINT_MAX - 3600LL);
#pragma region TVTComment
					tvtComment->OnForward(tm);
#pragma endregion
					// date属性値は秒精度しかないのでコメント表示が団子にならないよう適当にごまかす
					commentWindow_.ScatterLatestChats(1000);
				}
				commentWindow_.Update();
				bPendingTimerForward_ = false;
			}
			break;
		case TIMER_SETUP_CURJK:
			// 視聴状態が変化したので視聴中のサービスに対応する実況IDを調べて変更する
			KillTimer(hwnd, TIMER_SETUP_CURJK);

#pragma region TVTComment
			tvtComment->OnChannelListChange();
			tvtComment->OnChannelSelectionChange();
#pragma endregion
			break;
		case TIMER_DONE_MOVE:
			KillTimer(hwnd, TIMER_DONE_MOVE);
			commentWindow_.OnParentMove();
			break;
		case TIMER_DONE_SIZE:
			KillTimer(hwnd, TIMER_DONE_SIZE);
			commentWindow_.OnParentSize();
			bHalfSkip_ = GetWindowHeight(FindVideoContainer()) >= s_.halfSkipThreshold;
			break;
		}
		break;
	case WM_RESET_STREAM:
		dprintf(TEXT("CViewer::ForceDialogProcMain() WM_RESET_STREAM\n")); // DEBUG
		{
			lock_recursive_mutex lock(streamLock_);
			llftTot_ = -1;
		}
		return TRUE;
	}
	return DefWindowProc(hwnd, uMsg, wParam, lParam);
}

// ストリームコールバック(別スレッド)
BOOL CALLBACK CViewer::StreamCallback(BYTE *pData, void *pClientData)
{
	CViewer *pThis = static_cast<CViewer*>(pClientData);
	int pid = ((pData[1]&0x1F)<<8) | pData[2];
	BYTE bTransportError = pData[1]&0x80;
	BYTE bPayloadUnitStart = pData[1]&0x40;
	BYTE bHasAdaptation = pData[3]&0x20;
	BYTE bHasPayload = pData[3]&0x10;
	BYTE bAdaptationLength = pData[4];
	BYTE bPcrFlag = pData[5]&0x10;

	// シークやポーズを検出するためにPCRを調べる
	if (bHasAdaptation && bAdaptationLength >= 5 && bPcrFlag && !bTransportError) {
		DWORD pcr = (static_cast<DWORD>(pData[5+1])<<24) | (pData[5+2]<<16) | (pData[5+3]<<8) | pData[5+4];
		// 参照PIDのPCRが現れることなく5回別のPCRが出現すれば、参照PIDを変更する
		if (pid != pThis->pcrPid_) {
			int i = 0;
			for (; pThis->pcrPids_[i] >= 0; ++i) {
				if (pThis->pcrPids_[i] == pid) {
					if (++pThis->pcrPidCounts_[i] >= 5) {
						pThis->pcrPid_ = pid;
					}
					break;
				}
			}
			if (pThis->pcrPids_[i] < 0 && i + 1 < _countof(pThis->pcrPids_)) {
				pThis->pcrPids_[i] = pid;
				pThis->pcrPidCounts_[i] = 1;
				pThis->pcrPids_[++i] = -1;
			}
		}
		if (pid == pThis->pcrPid_) {
			pThis->pcrPids_[0] = -1;
		}
		//dprintf(TEXT("CViewer::StreamCallback() PCR\n")); // DEBUG
		lock_recursive_mutex lock(pThis->streamLock_);
		DWORD tick = GetTickCount();
		// 2秒以上PCRを取得できていない→ポーズから回復?
		bool bReset = tick - pThis->pcrTick_ >= 2000;
		pThis->pcrTick_ = tick;
		if (pid == pThis->pcrPid_) {
			long long pcrDiff = CounterDiff(pcr, pThis->pcr_);
			// ラップアラウンド近傍を特別扱いする必要はない(またいでシークする場合だってある)

			if (bReset || 0 <= pcrDiff && pcrDiff < 45000) {
				// 1秒以内は通常の再生と見なす
			} else if (abs(pcrDiff) < 15 * 60 * 45000) {
				// -15～0分、+1秒～15分PCRが飛んでいる場合、シークとみなし、
				// シークした分だけTOTをずらして読み込み直す
				if (pThis->llftTot_ >= 0 && pThis->llftTotPending_ != -1) {
					long long totDiff = pcrDiff * FILETIME_MILLISECOND / 45;
					pThis->llftTot_ += totDiff;
					pThis->totPcr_ += pcr - pThis->pcr_;
					if (pThis->llftTotLast_ >= 0) {
						pThis->llftTotLast_ += totDiff;
					}
					// 保留中のTOTはシーク後に取得した可能性があるので捨てる(再生速度の推定が狂ってコメントが大量に流れたりするのを防ぐため)
					pThis->llftTotPending_ = -2;
					pThis->bResyncComment_ = true;
				} else {
					bReset = true;
				}
			} else {
				// それ以上飛んでたら別ストリームと見なしてリセット
				bReset = true;
			}
			// 保留中のTOTはPCRの取得後に利用可能(llftTot_にシフト)にする
			if (pThis->llftTot_ >= 0) {
				if (pThis->llftTotPending_ >= 0) {
					pThis->llftTotLast_ = pThis->llftTot_;
					pThis->llftTot_ = pThis->llftTotPending_;
					pThis->totTickLast_ = pThis->totTick_;
					pThis->totTick_ = pThis->totTickPending_;
					// TOTの変化と対応するPCRの変化が5秒以上食い違っている場合、TOTがジャンプしたとみなして前回TOTを捨てる
					if (abs((pThis->llftTot_ - pThis->llftTotLast_) / FILETIME_MILLISECOND - CounterDiff(pcr, pThis->totPcr_) / 45) >= 5000) {
						pThis->llftTotLast_ = -1;
						// 状況はシークとそう違わないので読み直しも必要
						// 食い違い地点をまたいでシークすると二度読み直しが発生するが、解決は簡単ではなさそうなので保留
						pThis->bResyncComment_ = true;
					}
				}
				if (pThis->llftTotPending_ != -2) {
					// llftTot_に対応するPCRを取得済みであることを示す
					pThis->llftTotPending_ = -2;
					pThis->totPcr_ = pcr;
				}
			}
			pThis->pcr_ = pcr;
		}
		if (bReset) {
			// TOTを取得できていないことを表す
			pThis->llftTot_ = -1;
			PostMessage(pThis->hDummy_, WM_RESET_STREAM, 0, 0);
		}
	}

	// TOTパケットは地上波の実測で6秒に1個程度
	// ARIB規格では最低30秒に1個
	if (pid == 0x14 && bPayloadUnitStart && bHasPayload && !bTransportError) {
		BYTE *pPayload = pData + 4;
		if (bHasAdaptation) {
			// アダプテーションフィールドをスキップする
			if (bAdaptationLength > 182) {
				pPayload = nullptr;
			} else {
				pPayload += 1 + bAdaptationLength;
			}
		}
		if (pPayload) {
			BYTE *pTable = pPayload + 1 + pPayload[0];
			// TOT or TDT (ARIB STD-B10)
			if (pTable + 7 < pData + 188 && (pTable[0] == 0x73 || pTable[0] == 0x70)) {
				// TOT時刻とTickカウントを記録する
				LONGLONG llft = AribToFileTime(&pTable[3]);
				if (llft >= 0) {
					// UTCに変換
					llft += -32400000LL * FILETIME_MILLISECOND;
					dprintf(TEXT("CViewer::StreamCallback() TOT\n")); // DEBUG
					lock_recursive_mutex lock(pThis->streamLock_);
					// 時刻が変化したときだけ
					if (llft != pThis->llftTot_) {
						pThis->llftTotPending_ = llft;
						pThis->totTickPending_ = GetTickCount();
						if (pThis->llftTot_ < 0) {
							// 初回だけ速やかに取得
							pThis->llftTot_ = pThis->llftTotPending_;
							pThis->totTick_ = pThis->totTickPending_;
							pThis->llftTotLast_ = -1;
							pThis->llftTotPending_ = -1;
							pThis->totPcr_ = 0;
						}
					}
				}
			}
		}
	}
	return TRUE;
}

TVTest::CTVTestPlugin *CreatePluginClass()
{
	return new CViewer();
}
