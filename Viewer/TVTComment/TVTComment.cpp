#include "stdafx.h"
#include "TVTComment.h"
#include "IPC/IPCMessage/ChannelListIPCMessage.h"
#include "IPC/IPCMessage/ChatIPCMessage.h"
#include "IPC/IPCMessage/ChannelSelectIPCMessage.h"
#include "IPC/IPCMessage/CurrentChannelIPCMessage.h"
#include "IPC/IPCMessage/TimeIPCMessage.h"
#include "IPC/IPCMessage/CloseIPCMessage.h"
#include "IPC/IPCMessage/SetChatOpacityIPCMessage.h"
#include "IPC/IPCMessage/CommandIPCMessage.h"
#include "IPC/IPCMessageDecodeError.h"
#include "Utils.h"
#include <functional>
#include <exception>
#include <thread>
#include <ppl.h>

namespace TVTComment
{
	std::wstring_convert<std::codecvt_utf8_utf16<wchar_t>, wchar_t> TVTComment::utf8_utf16_conv;

	//Task内から呼び出す前提で作られている
	//接続完了までブロックするが、キャンセルされた場合はconcurrency::task_canceled例外を送出する
	void TVTComment::doConnect()
	{
		for (int i = 0;; i++)
		{
			DWORD pid=0;

			concurrency::interruption_point();
			try
			{
				//プロセス間通信に使うパイプ名をUUIDから作る
				UUID uuid;
				RPC_WSTR uuidStr;
				std::wstring receivePipeName=LR"(\\.\pipe\TVTComment_Up_)";
				std::wstring sendPipeName = LR"(\\.\pipe\TVTComment_Down_)";
				UuidCreate(&uuid);
				UuidToStringW(&uuid, &uuidStr);
				receivePipeName.append((const wchar_t *)uuidStr);
				sendPipeName.append((const wchar_t *)uuidStr);
				RpcStringFreeW(&uuidStr);
					
				this->ipcTunnel.reset(new IPCTunnel(sendPipeName,receivePipeName));

				PROCESS_INFORMATION pi = { 0 };
				STARTUPINFO si = { sizeof(STARTUPINFO) };
				if (CreateProcessW(NULL, &(this->collectExePath + L" " + receivePipeName.substr(9) + L" " + sendPipeName.substr(9))[0], NULL, NULL, FALSE, NORMAL_PRIORITY_CLASS, NULL, NULL, &si, &pi) == 0)
					throw std::system_error(std::system_error(GetLastError(), std::system_category()));
				pid = pi.dwProcessId;

				this->ipcTunnel->Connect();
			}
			catch (std::system_error)
			{
				if (pid != 0)
					Utils::CloseProcessById(pid);

				concurrency::interruption_point();

				//一定回数試してすべて失敗したらエラー
				if (i > 5)
					throw;

				//もう一回試してみる
				std::this_thread::sleep_for(std::chrono::seconds(1));
				continue;
			}
			this->isConnected = true;
			break;
		}
	}

	void TVTComment::receiveLoop()
	{
		//メッセージ受信ループ
		try
		{
			while (true)
			{
				try
				{
					std::unique_ptr<IIPCMessage> receivedMessage = this->ipcTunnel->Receive();
					processReceivedMessage(*receivedMessage);
				}
				catch (IPCMessageDecodeError &)
				{
					PostMessage(this->dialog, WM_USERINTERACTIONREQUEST, (WPARAM)UserInteractionRequestType::InvalidMessage, 0);
					errorCount++;
					if (errorCount >= FETALERROR_COUNT)
						throw;

				}
				catch (std::system_error &)
				{
					if (this->isClosing)
						throw;//相手が接続を切った

					PostMessage(this->dialog, WM_USERINTERACTIONREQUEST, (WPARAM)UserInteractionRequestType::ReceiveError, 0);
					errorCount++;
					if (errorCount >= FETALERROR_COUNT)
						throw;
				}
			}
		}
		catch (std::exception &e)
		{
			//何らかのエラーが起こった時、相手が接続を切った時
			if (!this->isClosing)
			{
				static std::wstring_convert<std::codecvt<wchar_t, char, mbstate_t >> char_wide_conv;

				std::wstring what = char_wide_conv.from_bytes(e.what());
				wchar_t *text = new wchar_t[what.size() + 1];
				what.copy(text, sizeof(text) / sizeof(text[0]));
				text[what.size()] = L'\0';
				PostMessage(this->dialog, WM_USERINTERACTIONREQUEST, (WPARAM)UserInteractionRequestType::FetalErrorInTask, (LPARAM)text);
			}
			PostMessage(this->dialog, WM_DISABLEPLUGIN, 0, 0);
		}
	}

	void TVTComment::processReceivedMessage(const IIPCMessage &msg)
	{
#pragma warning(push)
#pragma warning(disable:4456)//変数messageの宣言が被っているのに対する警告抑止
		if (auto message = dynamic_cast<const ChatIPCMessage *>(&msg))
		{
			const Chat &chat = message->Chat;
			
			commentWindow->AddChat(utf8_utf16_conv.from_bytes(chat.text).c_str(), chat.color.GetColorRef(),
				(chat.position == Chat::Position::Default) ? CCommentWindow::CHAT_POS_DEFAULT : (chat.position == Chat::Position::Bottom) ? CCommentWindow::CHAT_POS_SHITA : CCommentWindow::CHAT_POS_UE,
				(chat.size == Chat::Size::Small) ? CCommentWindow::CHAT_SIZE_SMALL : CCommentWindow::CHAT_SIZE_DEFAULT,chat.self);
			commentWindow->ScatterLatestChats(1000);
		}
		else if (auto message = dynamic_cast<const ChannelSelectIPCMessage *>(&msg))
		{
			this->tvtest->SetChannel(message->SpaceIndex, message->ChannelIndex,message->ServiceId);
		}
		else if (auto message = dynamic_cast<const CloseIPCMessage *>(&msg))
		{
			this->isClosing = true;
			//sendMessage(msg);
			PostMessage(this->dialog, WM_DISABLEPLUGIN, 0, 0);
		}
		else if (auto message = dynamic_cast<const SetChatOpacityIPCMessage *>(&msg))
		{
			lastOpacity = (WPARAM)message->Opacity;
			this->tvtest->SetPluginCommandState(static_cast<int>(Command::HideComment), 0);
			PostMessage(this->dialog, WM_SETCHATOPACITY, (WPARAM)message->Opacity, 0);
		}
#pragma warning(pop)
	}

	bool TVTComment::sendMessage(const IIPCMessage &msg)
	{
		//closing状態ならCloseIPCMessage以外は送らない
		if (this->isClosing && dynamic_cast<const CloseIPCMessage *>(&msg) == nullptr)
			return false;

		try
		{
			this->ipcTunnel->Send(msg);
		}
		catch (std::system_error)
		{
			PostMessage(this->dialog, WM_USERINTERACTIONREQUEST, (WPARAM)UserInteractionRequestType::SendError, 0);
			return false;
		}
		return true;
	}

	std::time_t TVTComment::SystemTimeToUnixTime(const SYSTEMTIME &time)
	{
		FILETIME ft;
		::SystemTimeToFileTime(&time, &ft);
		return ::FileTimeToUnixTime(::FileTimeToLongLong(ft));
	}

	bool TVTComment::IsConnected() const
	{
		return this->isConnected;
	}

	TVTComment::TVTComment(TVTest::CTVTestApp *tvtest,CCommentWindow *commentWindow, const std::wstring &collectExePath)
		:tvtest(tvtest),commentWindow(commentWindow),dialog(0),lastTOT(0),lastEventId(0),errorCount(0),ipcTunnel(nullptr),isConnected(false),isClosing(false),collectExePath(collectExePath)
	{
	}

	INT_PTR TVTComment::WindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM /* lParam */)
	{
		switch (uMsg)
		{
		case WM_CREATE:
			this->dialog = hwnd;

			this->asyncTask = concurrency::task<void>([this]
			{
				try
				{
					doConnect();
				}
				catch (concurrency::task_canceled)
				{
					//接続処理中にキャンセルされた
					throw;
				}
				catch (std::system_error &e)
				{
					//接続が失敗した
					std::wstring_convert<std::codecvt<wchar_t, char, mbstate_t >> char_wide_conv;

					std::wstring what = char_wide_conv.from_bytes(e.what());
					wchar_t *text = new wchar_t[what.size() + 1];
					what.copy(text, sizeof(text) / sizeof(text[0]));
					text[what.size()] = L'\0';

					PostMessage(this->dialog, WM_USERINTERACTIONREQUEST, (WPARAM)UserInteractionRequestType::ConnectFail, (LPARAM)text);
					PostMessage(this->dialog, WM_DISABLEPLUGIN, 0, 0);
					throw;
				}

				//正常に接続が完了した
				PostMessage(this->dialog, WM_USERINTERACTIONREQUEST, (WPARAM)UserInteractionRequestType::ConnectSucceed, 0);
				this->isConnected = true;

				PostMessage(this->dialog, WM_ONCHANNELLISTCHANGE, 0, 0);
				PostMessage(this->dialog, WM_ONCHANNELSELECTIONCHANGE, 0, 0);
				
				//受信ループ開始
				this->receiveLoop();
			}, cancel.get_token());
			break;

		case WM_DESTROY:
			this->dialog = 0;
			break;

		case WM_USERINTERACTIONREQUEST:
		{			
			switch ((UserInteractionRequestType)wParam)
			{
			case UserInteractionRequestType::ConnectFail:
			{
				std::wstring text = L"TVTComment設定ウィンドウとの接続に失敗しました。プラグインを無効化します。";
				MessageBoxW(this->tvtest->GetAppWindow(), text.c_str(), L"TVTComment表示側エラー", 0);
				break;
			}
			case UserInteractionRequestType::InvalidMessage:
				this->commentWindow->AddChat(TEXT("[TVTComment間通信で無効なメッセージを受信しました]"), RGB(255, 0, 0), CCommentWindow::CHAT_POS_UE, CCommentWindow::CHAT_SIZE_SMALL);
				break;
			case UserInteractionRequestType::ReceiveError:
				this->commentWindow->AddChat(TEXT("[TVTComment間通信でメッセージの受信に失敗しました]"), RGB(255, 0, 0), CCommentWindow::CHAT_POS_UE, CCommentWindow::CHAT_SIZE_SMALL);
				break;
			case UserInteractionRequestType::SendError:
				this->commentWindow->AddChat(TEXT("[TVTComment間通信でメッセージの送信に失敗しました]"), RGB(255, 0, 0), CCommentWindow::CHAT_POS_UE, CCommentWindow::CHAT_SIZE_SMALL);
				break;
			case UserInteractionRequestType::FetalErrorInTask:
			{
				std::wstring text = L"TVTComment設定ウィンドウからの受信処理で致命的な問題が発生しました。\nプラグインを無効化します。";
				MessageBoxW(this->tvtest->GetAppWindow(), text.c_str(), L"TVTComment表示側エラー", 0);
				break;
			}
			}
			return TRUE;
		}

		case WM_DISABLEPLUGIN:
			this->tvtest->EnablePlugin(false);
			break;

		case WM_ONCHANNELLISTCHANGE:
			this->OnChannelListChange();
			break;

		case WM_ONCHANNELSELECTIONCHANGE:
			this->OnChannelSelectionChange();
			break;
		}

		return FALSE;
	}

	void TVTComment::OnChannelListChange()
	{
		if (!this->isConnected)
			return;

		this->channelList.clear();
		int spaceNum;
		tvtest->GetTuningSpace(&spaceNum);
		for (int space = 0; space < spaceNum; ++space)
		{
			for (int index = 0;; ++index)
			{
				TVTest::ChannelInfo channelInfo;
				if (!tvtest->GetChannelInfo(space, index, &channelInfo))
					break;
				ChannelInfo ci;
				ci.SpaceIdx = space;
				ci.ChannelIdx = index;
				ci.RemoteControlKeyID = channelInfo.RemoteControlKeyID;
				ci.NetworkID = channelInfo.NetworkID;
				ci.TransportStreamID = channelInfo.TransportStreamID;
				ci.NetworkName = utf8_utf16_conv.to_bytes(channelInfo.szNetworkName);
				ci.TransportStreamName = utf8_utf16_conv.to_bytes(channelInfo.szTransportStreamName);
				ci.ChannelName = utf8_utf16_conv.to_bytes(channelInfo.szChannelName);
				ci.ServiceID = channelInfo.ServiceID;
				ci.Hidden = channelInfo.Flags & TVTest::CHANNEL_FLAG_DISABLED;

				this->channelList.push_back(std::move(ci));
			}
		}

		ChannelListIPCMessage msg;
		msg.ChannelList = this->channelList;
		this->sendMessage(msg);
	}

	void TVTComment::OnChannelSelectionChange()
	{
		if (!this->isConnected)
			return;

		TVTest::ChannelInfo ci;
		ci.Size = sizeof(ci);
		this->tvtest->GetCurrentChannelInfo(&ci);

		TVTest::ProgramInfo pi;
		wchar_t eventName[128];
		wchar_t eventText[512];
		wchar_t eventExtText[2048];
		pi.Size = sizeof(pi);
		pi.pszEventName = eventName;
		pi.MaxEventName = sizeof(eventName) / sizeof(eventName[0]);
		pi.pszEventText = eventText;
		pi.MaxEventText = sizeof(eventText) / sizeof(eventText[0]);
		pi.pszEventExtText = eventExtText;
		pi.MaxEventExtText = sizeof(eventExtText) / sizeof(eventExtText[0]);
		this->tvtest->GetCurrentProgramInfo(&pi);

		sendCurrentChannelIPCMessage(ci, pi);
	}

	//TOT時刻の更新間隔より短い間隔で定期的に呼ぶ
	void TVTComment::OnForward(std::time_t tot)
	{
		if (!this->isConnected)
			return;

		//Event(番組)が変わってたら通知
		TVTest::ProgramInfo pi;
		pi.Size = sizeof(pi);
		pi.pszEventName = NULL;
		pi.pszEventText = NULL;
		pi.pszEventExtText = NULL;
		this->tvtest->GetCurrentProgramInfo(&pi);//EventIDだけ取れればいい
		if (pi.EventID != lastEventId)
		{
			//EventIDが変わっていたら
			TVTest::ChannelInfo ci;
			ci.Size = sizeof(ci);
			this->tvtest->GetCurrentChannelInfo(&ci);

			wchar_t eventName[128];
			wchar_t eventText[512];
			wchar_t eventExtText[2048];
			pi.Size = sizeof(pi);
			pi.pszEventName = eventName;
			pi.MaxEventName = sizeof(eventName) / sizeof(eventName[0]);
			pi.pszEventText = eventText;
			pi.MaxEventText = sizeof(eventText) / sizeof(eventText[0]);
			pi.pszEventExtText = eventExtText;
			pi.MaxEventExtText = sizeof(eventExtText) / sizeof(eventExtText[0]);
			this->tvtest->GetCurrentProgramInfo(&pi);

			sendCurrentChannelIPCMessage(ci, pi);
		}
		
		//Totが変わってたら通知
		if (tot != lastTOT)
		{
			this->lastTOT = tot;

			TimeIPCMessage msg;
			msg.Time = tot;
			this->sendMessage(msg);
		}
	}

	void TVTComment::OnCommandInvoked(Command command)
	{
		CommandIPCMessage msg;
		switch (command)
		{
			case Command::ShowWindow:
				msg.CommandId = "ShowWindow";
				this->sendMessage(msg);
				break;
			case Command::HideComment:
				if (this->commentWindow->GetOpacity() != 0) {
					lastOpacity = this->commentWindow->GetOpacity();
					PostMessage(this->dialog, WM_SETCHATOPACITY, 0, 0);
					this->tvtest->SetPluginCommandState(static_cast<int>(Command::HideComment), TVTest::COMMAND_ICON_STATE_CHECKED);
				}
				else {
					PostMessage(this->dialog, WM_SETCHATOPACITY, lastOpacity, 0);
					this->tvtest->SetPluginCommandState(static_cast<int>(Command::HideComment), 0);
				}
				break;
		}
	}

#pragma warning(push)
#pragma warning(disable: 4701)//未初期化のローカル変数siを使っているとする警告の抑止
	void TVTComment::sendCurrentChannelIPCMessage(const TVTest::ChannelInfo &ci, const TVTest::ProgramInfo &pi)
	{
		if (!this->isConnected)
			return;
		TVTest::ServiceInfo si;
		si.Size = sizeof(si);
		int serviceIdx=this->tvtest->GetService();
		if (serviceIdx < 0) //チャンネル変更中などには情報が壊れてるので壊れてるときは抜けるように変更
			return;
		this->tvtest->GetServiceInfo(serviceIdx, &si);

		CurrentChannelIPCMessage msg;
		msg.SpaceIndex = ci.Space;
		msg.ChannelIndex = ci.Channel;
		msg.RemotecontrolkeyId = ci.RemoteControlKeyID;

		msg.NetworkId = ci.NetworkID;
		msg.TransportstreamId = ci.TransportStreamID;
		msg.ServiceId = serviceIdx == -1 ? ci.ServiceID : si.ServiceID;
		msg.EventId = pi.EventID;

		if(ci.NetworkID!=0)//チャンネルスキャンしてない（File系BonDriverなど）とNID==0でNetworkNameも正しい値を返さない
			msg.NetworkName.assign(utf8_utf16_conv.to_bytes(ci.szNetworkName));
		if(ci.TransportStreamID!=0)//チャンネルスキャンしてない（File系BonDriverなど）とTSID==0でTransportStreamNameも正しい値を返さない
			msg.TransportstreamName.assign(utf8_utf16_conv.to_bytes(ci.szTransportStreamName));
		if(serviceIdx!=-1)
			msg.ServiceName.assign(utf8_utf16_conv.to_bytes(si.szServiceName));
		msg.ChannelName.assign(utf8_utf16_conv.to_bytes(ci.szChannelName));

		msg.EventName.assign(utf8_utf16_conv.to_bytes(pi.pszEventName));
		msg.EventText.assign(utf8_utf16_conv.to_bytes(pi.pszEventText));
		msg.EventExtText.assign(utf8_utf16_conv.to_bytes(pi.pszEventExtText));

		msg.StartTime = SystemTimeToUnixTime(pi.StartTime);
		msg.Duration = pi.Duration;

		//最後に送ったEventIDを記憶
		this->lastEventId = pi.EventID;

		this->sendMessage(msg);
	}
#pragma warning(pop)
	
	TVTComment::~TVTComment() noexcept
	{
		try
		{
			this->isClosing = true;
		}
		catch (...) {}
		try
		{
			while (!this->asyncTask.is_done())
			{
				sendMessage(CloseIPCMessage());
				std::this_thread::sleep_for(std::chrono::milliseconds(100));
			}
			this->asyncTask.wait();
		}
		catch (...) {}
		try
		{
			delete this->ipcTunnel.release();
		}
		catch(...){}
	}
}
