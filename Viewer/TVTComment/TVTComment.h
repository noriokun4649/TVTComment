#pragma once
#include "../NicoJK/TVTestPlugin.h"
#include "../NicoJK/Util.h"
#include "../NicoJK/CommentWindow.h"
#include "IPC/IPCTunnel.h"
#include "ChannelInfo.h"
#include <vector>
#include <locale>
#include <codecvt>
#include <memory>
#include <atomic>
#include <thread>
#include <ppltasks.h>

#ifndef UNICODE
#error Character set must be Unicode!
#endif

namespace TVTComment
{
	enum class Command
	{
		ShowWindow,
		HideComment
	};

	enum class Panel
	{
		TvTComment,
	};

	//TVTCommentの最上位クラス
	//このクラスのインスタンスの生存期間は勢いウィンドウのより長い必要がある
	//エラー時にプラグインを無効化することがある
	class TVTComment
	{
	private:
		TVTest::CTVTestApp *tvtest;
		CCommentWindow *commentWindow;
		HWND dialog;

		std::time_t lastTOT;
		uint16_t lastEventId;

		WPARAM lastOpacity;
		int errorCount;//受信エラーが起きた回数
		static constexpr int FETALERROR_COUNT = 10;//errorCountがこの値以上になるとプラグインを無効化する

		std::atomic<bool> isClosing;//ClosingIPCMessageを受け取りプラグインを閉じようとしている

		std::atomic<bool> isConnected;
		std::unique_ptr<IPCTunnel> ipcTunnel;

		std::vector<ChannelInfo> channelList;

		concurrency::cancellation_token_source cancel;//このTVTCommentクラスのインスタンスをdeleteするときに使う
		concurrency::task<void> asyncTask;

		std::wstring collectExePath;//起動するEXEのパス

		static std::wstring_convert<std::codecvt_utf8_utf16<wchar_t>, wchar_t> utf8_utf16_conv;

	private:
		void doConnect();
		void receiveLoop();
		void processReceivedMessage(const IIPCMessage &msg);
		bool sendMessage(const IIPCMessage &msg);

		static std::time_t SystemTimeToUnixTime(const SYSTEMTIME &time);
		void sendCurrentChannelIPCMessage(const TVTest::ChannelInfo &ci,const TVTest::ProgramInfo &pi);

	public:
		static constexpr int WM_USERINTERACTIONREQUEST = WM_APP + 1001;//ダイアログボックスの表示などユーザーへのメッセージを表示する
		static constexpr int WM_DISABLEPLUGIN = WM_APP + 1002;//プラグインを無効化する（別スレッドから無効化するときに使う）
		static constexpr int WM_ONCHANNELLISTCHANGE = WM_APP + 1003;//メンバ関数OnChannelListChangeを呼ぶ（別スレッドから送るときに使う）
		static constexpr int WM_ONCHANNELSELECTIONCHANGE = WM_APP + 1004;//メンバ関数OnChannelSelectionChangeを呼ぶ（別スレッドから送るときに使う）
		static constexpr int WM_SETCHATOPACITY = WM_APP + 1005;//コメント透過度を設定する wParamに透過度を渡す(0~255)

		enum class UserInteractionRequestType{ConnectSucceed,ConnectFail,InvalidMessage,ReceiveError,SendError, FetalErrorInTask};

	public:
		bool IsConnected() const;
		TVTComment(TVTest::CTVTestApp *tvtest,CCommentWindow *commentWindow,const std::wstring &collectExePath);
		INT_PTR WindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam);
		void OnChannelListChange();
		void OnChannelSelectionChange();
		void OnForward(std::time_t tot);//TOT時刻の更新間隔より細かい間隔で呼ぶ
		void OnCommandInvoked(Command command);
		~TVTComment() noexcept;
	};
}
