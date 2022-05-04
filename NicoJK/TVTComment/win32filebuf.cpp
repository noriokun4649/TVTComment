#include "stdafx.h"
#include "win32filebuf.h"
#include <cstring>
#include <algorithm>

template<typename charT, typename traits>
basic_win32filebuf<charT,traits>::basic_win32filebuf(HANDLE handle,bool close_handle,bool use_overlapped)
	:handle(handle),close_handle(close_handle),use_overlapped(use_overlapped)
{
	setp(this->outBuffer.data(), this->outBuffer.data()+this->outBuffer.size());
	setg(this->inBuffer.data(),this->inBuffer.data(), this->inBuffer.data());
}

template<typename charT, typename traits>
int basic_win32filebuf<charT, traits>::sync()
{
	return (this->dowrite()) ? 0 : -1;
}

template<typename charT, typename traits>
std::streamsize basic_win32filebuf<charT, traits>::xsputn(const typename basic_win32filebuf<charT, traits>::char_type *s, std::streamsize n)
{
	std::streamsize ret=0;
	while (true)
	{
		std::streamsize charsFree=this->epptr()-this->pptr();
		if (charsFree<n-ret)
		{
			//sに残っているデータをすべてバッファに書き込むとバッファの空きを超えるなら
			//バッファの空きの分だけ書き込んで、バッファを消費するためoverflowを呼ぶ
			traits::copy(this->pptr(), s + ret, (std::size_t)charsFree);
			this->pbump((int)charsFree);
			ret += charsFree;
			if (this->overflow(*(s + ret)) == traits::eof())
				return ret;
		}
		else
		{
			//sに残っているデータをすべてバッファに書き込んでもまだバッファの大きさにとどかないなら
			//残りをすべて書き込む
			traits::copy(this->pptr(), s + ret, (std::size_t)(n - ret));
			this->pbump((int)(n - ret));
			return n;
		}
	}
}

template<typename charT, typename traits>
std::streamsize basic_win32filebuf<charT, traits>::xsgetn(typename basic_win32filebuf<charT, traits>::char_type *s, std::streamsize n)
{
	std::streamsize ret = 0;
	while (true)
	{
		std::streamsize charsLeft = this->egptr() - this->gptr();
		if (ret+charsLeft < n)
		{
			//バッファをすべてsに書き込んでもまだnにとどかないなら
			//今あるバッファをすべて書き込んで、新たにバッファにデータを乗せるためunderflowを呼ぶ
			traits::copy(s+ret, this->gptr(), (std::size_t)charsLeft);
			this->gbump((int)charsLeft);
			ret += charsLeft;
			if (this->underflow() == traits::eof())
				return ret;
		}
		else
		{
			//バッファをすべてsに書き込むとnを超えてしまうなら
			//書き込めるだけ書き込む
			traits::copy(s+ret, this->gptr(), (std::size_t)(n - ret));
			this->gbump((int)(n - ret));
			return n;
		}
	}
}

template<typename charT, typename traits>
typename basic_win32filebuf<charT, traits>::int_type basic_win32filebuf<charT, traits>::overflow(typename basic_win32filebuf<charT, traits>::int_type c)
{
	if (!this->dowrite() || c==traits::eof())
		return traits::eof();

	*this->pptr() = traits::to_char_type(c);
	//this->pbump(1);
	return c;
}

template<typename charT, typename traits>
typename basic_win32filebuf<charT, traits>::int_type basic_win32filebuf<charT, traits>::underflow()
{
	if (!this->doread())
		return traits::eof();

	charT ret = *this->gptr();
	//this->gbump(1);
	return traits::to_int_type(ret);
}


template<typename charT,typename traits>
bool basic_win32filebuf<charT,traits>::dowrite()
{
	charT *base = this->pbase();
	while (base<this->pptr())
	{
		DWORD bytesProcessed;
		if (WriteFile(this->handle, base, (DWORD)((this->pptr() - base) * sizeof(charT)), &bytesProcessed, NULL) == 0)
		{
			charT *cur = this->pptr();
			this->setp(base, this->epptr());
			this->pbump((int)(cur - base));
			return false;
		}

		base = (charT *)(((char *)base) + bytesProcessed);
	}

	this->setp(this->outBuffer.data(), this->outBuffer.data() + this->outBuffer.size());

	return true;
}

template<typename charT, typename traits>
bool basic_win32filebuf<charT, traits>::doread()
{
	DWORD bytesProcessed;
	if (ReadFile(this->handle, this->eback(),(DWORD)( this->inBuffer.size() * sizeof(charT)), &bytesProcessed, NULL) == 0 || bytesProcessed==0)
		return false;
	this->setg(this->eback(), this->eback(), (charT *)(((char *)this->eback()) + bytesProcessed));

	return true;
}

template<typename charT,typename traits>
HANDLE basic_win32filebuf<charT, traits>::gethandle() const noexcept
{
	return this->handle;
}

template<typename charT, typename traits>
basic_win32filebuf<charT, traits>::~basic_win32filebuf() noexcept
{
	if (this->close_handle)
		CloseHandle(this->handle);
}

template basic_win32filebuf<char>;
template basic_win32filebuf<wchar_t>;