#pragma once
#include "IPCProtocolStream.h"
#include "IPCMessage/IIPCMessage.h"
#include "../win32filestream.h"
#include <memory>
#include <exception>
#include <atomic>

namespace TVTComment
{

	class IPCTunnelConnectError :public std::exception
	{
	private:
		std::string what_arg;
	public:
		explicit IPCTunnelConnectError(const std::string &what_arg):what_arg(what_arg){}

		virtual const char *what() const override { return what_arg.c_str(); }
	};

	//プロセス間通信トンネル
	//Connectが完了する前にSendやReceiveを呼ぶと未定義動作
	//SendとReceive自体の再入はできないが別スレッドからSendとReceiveを同時に呼んでOK
	class IPCTunnel
	{
	private:
		IPCProtocolStream upstream;
		IPCProtocolStream downstream;
		std::atomic<HANDLE> connectThread;
		std::atomic<HANDLE> sendThread;
		std::atomic<HANDLE> receiveThread;

	public:
		IPCTunnel(const std::wstring &sendPipeName,const std::wstring &receivePipeName);
		void Connect();
		void Send(const IIPCMessage &msg);
		std::unique_ptr<IIPCMessage> Receive();
		void Cancel() noexcept;//処理中のConnect,Send,Receiveをキャンセルし、std::system_errorで帰らせる
		~IPCTunnel() noexcept;//Cancelを呼んだ後
	};
}