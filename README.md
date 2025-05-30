# TvtComment　過去ログ取得追加版&Twitter実況対応版

TVTest ニコニコ & 5ch コメント表示プラグイン

- 利用は自己責任で行ってください
- このプラグインはニコニコのサーバや5chのサーバーとTwitterのサーバーとも通信します。サーバに高負荷を与えるような利用や改変をしないでください。

このプラグインは本家のTvtCommentに[非公式過去ログAPI](https://jikkyo.tsukumijima.net)から過去ログを取得する機能を追加した物です。  
さらに、Twitterリアルタイム実況の機能も追加しました。

## 本家との相違点
- ~~非公式過去ログ実装~~ (現在は本家の実装を利用中)
- 勢いの実装
- Twitter実況の実装
    - Twitter認証周りのUI実装
    - 自動検索モード実装
    - ツイート投稿実装
    - NG設定にハッシュタグ、メンションの改変実装
    - Annictからハッシュタグを自動取得する機能を実装
- NG設定に指定文字数以上をNGにする設定の実装
- 絵文字表示への対応
- サイドパネルへアイコンの追加
- ニコニコ動画の2段階認証への対応
- ニコニコ生放送・実況のコメント受信をWebSocketへ移行
    - コミュニティ・チャンネルにおける自動再接続実装
- その他バグ修正等（幾つかは本家にマージ済み）

## ライセンス
本家同様MITライセンス

## ソースコード
[GitHub](https://github.com/noriokun4649/TVTComment)

## 開発環境
- TVTest 0.10.0 [4023008](https://github.com/DBCTRADO/TVTest/commit/402300897d009411a8738eb8278240b1b48a0681) x64 (MSVC:19.42.34438.0)
- LiblSDB [6fa3b9d](https://github.com/DBCTRADO/LibISDB/commit/6fa3b9d616d4fa739eb343908d4446504c8718f6) x64 (MSVC:19.42.34438.0)
- Microsoft Visual Studio Community 2022

## 謝辞
このソフトは[TvtComment](https://github.com/silane/TVTComment)から改造して作られました。また過去ログの取得は[非公式過去ログAPI](https://jikkyo.tsukumijima.net)より取得しております。
これら2つを作られた作者に感謝いたします。silane氏及びtsukumijima氏ありがとうございます。


## 編集者
noriokun4649

# TvtComment

TVTest ニコニコ & 5ch コメント表示プラグイン

- 利用は自己責任で行ってください
- このプラグインはニコニコのサーバや5chのサーバーと通信します。サーバに高負荷を与えるような利用や改変をしないでください。

このプラグインは本家のTvtCommentに[非公式過去ログAPI](https://jikkyo.tsukumijima.net)から過去ログを取得する機能を追加した物です。

## なにこれ？
ニコニコや5chのコメントをTVTestの画面に流すプラグインです。
5chのレスを5chAPIから取得することによる比較的低遅延な表示、録画視聴時の5chの過去ログからの自動でのレス表示、録画視聴時の[tsukumijimaさん提供の非公式ニコニコ実況過去ログAPI](https://jikkyo.tsukumijima.net/)からの自動でのコメント表示などが特徴です。
また有志によるBSの実況生放送にも対応しています。


## 機能
- 2020/12/16からのリニューアル後のニコニコ実況のコメントの表示および投稿
- 有志によるBS実況コミュニティの生放送のコメントの表示および投稿
- 指定したニコニコ生放送のコメントの表示および投稿
- 5chのスレッドのレスの表示
- 録画視聴時の5chの過去ログからの自動でのレス表示
- 録画視聴時の[tsukumijimaさん提供の非公式ニコニコ実況過去ログAPI](https://jikkyo.tsukumijima.net/)からの自動でのコメント表示
- ニコニココメント形式のXMLファイルからコメントを表示
- ユーザーNG、単語NG、色コメNGなどのNG機能


## 導入方法
1. ソフトを含んだzipファイルを[ここ](https://github.com/silane/TVTComment/releases)からダウンロードする
2. 入ってなければMicrosoft Visual C++ 2015 再頒布可能パッケージをインストールする
3. zipファイルのx86またはx64フォルダ内のTvtComment.tvtp、TvtComment.iniをTVTestのPluginsフォルダにいれる
4. TvtCommentフォルダをフォルダごとTVTestのPluginsフォルダにいれる（←***重要！***）
5. TVTestを起動してこのプラグインを有効にした後表示されるウィンドウから、「設定」タブ->「その他の設定」ボタンでニコニコのユーザーや5chAPIのHMKey、AppKeyの設定する (HMKeyとAppKeyは各自調べてください)。


## 改造元のNicoJKについて
このソフトはコメント描画部にNicoJK(rev.16)のコードを含んでいます。なのでNicoJK.iniのコメント描画に関する設定はTVTComment.iniでもそのまま使えるはずです。でもほとんどテストしてないので保証はできません。


## TvtComment.exeについて
このソフトは、コメント描画を行う実際のTVTestプラグイン部分と、GUI表示やコメント収集をする部分に分かれています。前者がTvtComment.tvtpで後者がTvtComment.exeです。だからTvtComment.exeは怪しいexeじゃないよって一応言っておきます。


## 使い方の分かりにくい部分
- ウィンドウの移動は背景部分をドラッグすればできます
- コメント改行はAlt+Enterで行えます
- チャンネルリストをダブルクリックでチャンネル変更できます
- TVTestのキー割り当てでウィンドウを前面に持ってくるキーを割り当てられます
- コメントログはコメントを選択しないとスクロールできません


## バグ
- 5chが板移転などでURLが変わってるとフリーズしたりするかも
- TVTestが閉じられてもウィンドウが閉じないことがある
- 設定ファイルが見つからなかったり内容がおかしいと何も言わずに落ちることがあるかも
- ニコニコ生放送・実況においてこのソフトで投稿したコメントがニコニコのウェブサイトの画面では表示されないことがある
    - ブラウザと同時ログインしている場合に起こる？
- NG設定画面で複数のルールを選択して「ルール削除」ボタンを押しても一つしか削除されない


## 制限
- 5chへの投稿はできません。スレッドのページをブラウザで開く機能があるのでそこで投稿してください。
- ニコニコ生放送は運営コメント等には対応していません
- 5ch過去ログでコメントが最初に表示されるまで数十秒かかる
- 5ch過去ログで「さかのぼる時間」設定によってはコメントが表示されない。「さかのぼる時間」設定を長くすると表示されますが2ch.scや5ch.netへの接続が増え与える負荷が増加しますし、最初にコメントが表示されるまでの時間も長くなります。


## ライセンス
LICENSE参照


## 開発環境
- TVTest 0.9.0
- Microsoft Visual Studio Community 2019


## ソースコード
[GitHub](https://github.com/silane/TVTComment)


## 謝辞
- このソフトは[NicoJK](https://github.com/rutice/NicoJK)から改造して作られました。NicoJKの作者様への感謝をここに表します。私はwin32プログラミングはほとんどできないので元のプログラムがなければ決して作れませんでした。ありがとうございます。
- ニコニコ実況過去ログ機能には[tsukumijimaさん提供の非公式ニコニコ実況過去ログAPI](https://jikkyo.tsukumijima.net/)を使わさせていただいています。素晴らしいサービスをありがとうございます。
