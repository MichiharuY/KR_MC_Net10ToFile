using System;
using System.Windows;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Reflection;    // アセンブリ情報の取得
using System.IO;    // ファイルIO

using KAISYOU;   // 共通モジュール

using static System.Convert;
using static System.Environment;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media.Animation;

// ファイル名のIndex
enum SAVEPATH
{
    保存先 = 0,
    転送先
}

// 時間計測情報（浚渫、推進自動油回収の時間計算で使用する）
public struct TimeInfo
{
    public bool OnOff;  // true : 時間計測中
    public DateTime startTime;  // 開始時刻
    public int idOld;   // 前回のID
}

namespace _01_NET10収録_NET10toFILE_
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        // 定数
        static readonly string LOGNAME = "Net10toFILE.log"; // ログファイル名

        // 収録フォルダ名（保存先、転送先）
        static readonly string[] ROOTNAME = { "N2F", "F2DB" };
        // 収録フォルダ名（上記の下の階層、区分毎にフォルダを作成し保存）
        static readonly string[] DIVNAME = { "DTMaster", "Net10Data" };

        static readonly int PING_TIMEOUT = 200; // Pingのタイムアウト時間[ms]

        // 変数
        private bool WStartup = true;           // 二重起動チェック用

        public NET10Address m_NET10Addr = null; // 収録アドレス管理クラス
        public NET10Control m_NET10Ctrl = null; // NET10通信管理クラス
#if false   // 今回不要
        public IDManager m_IDMag = null;        // 浚渫、油回収ID管理クラス
#endif
        public TransFile m_Trans = null;        // ファイル転送管理クラス
        public LogFile m_Log = null;            // ログ出力

        string[] saveFolder, savePath;          // 収録フォルダ、ファイルパス（区分毎）
        public short[] m_RcvBuf;                // 収録データ（NET10Controlから貰う）

        public bool m_AllowAppExit = false;     // アプリの終了フラグ（falseのままだとFormClosingでキャンセルされて終了できない）

        // 初期設定
        public string m_IPAddress = "";
        public string m_SourcePath = "";
        public string m_DestPath = "";

        // 収録関係
        public const int RecCountPerFile = 60;  // 1ファイルに収録するデータレコード数（行数）

        public int m_RecFileCount;  // 保存ファイルの収録レコード数（RecCountPerFile個になると値リセット）
        public int m_TotalRecCount;  // 起動後の収録レコード総数
        public int m_ActualRecCount = 0;    // 起動後の転送レコード総数
        TimeSpan m_RecTime; // 収録にかかった時間（UI表示用）

        public bool m_TransError;   // true : 転送先へのアクセス異常（Pingに失敗してます）
        public bool m_NowTrans;     // true : ファイル転送中（Formにてフラグを管理）
        public int m_TransState = 0;    // 転送状態（ステータスバーの表示用）
                                        // 0 : 一度も転送していない、1 : 正常に転送が行えている、2 : 転送先フォルダが見つからない

        public bool m_TransErrorOccurred = false;   // true : ファイル転送クラスでエラー発生(UI表示用)
        public int m_TransErrorCount = 0;   // 転送エラーの発生数（未使用）

        public int m_RecID;     // RecID(収録レコードのID)
        public DateTime m_SaveTimeOld = DateTime.Now;   // saveNeT10Dataの前回の実行日時、同一日時に実行された場合は処理を中断する

#if false   // 今回不要
        public int m_DredgeID;  // 浚渫回数
        public int m_OilID;     // 油回収回数
#endif

        // 時間計測情報（浚渫、推進自動油回収の時間計算で使用する）
        public TimeInfo[] m_TimeInfo = new TimeInfo[2];


        private DispatcherTimer timerUpdateUI;      // UI更新タイマー

        private string ExePath;                     // 実行ファイルPath

        private Storyboard mStoryboard;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            // 実行ファイルPath
            ExePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            ExePath = ExePath.Substring(0, ExePath.LastIndexOf("\\"));

            mStoryboard = (Storyboard)this.Resources["Storyboard1"];    // Storyboardを取得
            mStoryboard.Begin(this);                                    // アニメーション開始
            toolStripStatusLabel4.Visibility = Visibility.Hidden;       // アニメーション非表示
        }

        /// <summary>
        /// 初期処理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            bool ret = false;

            WStartup = false;                           // ここまできたら二重起動ではない

            this.Top = 0;
            this.Left = 862;

            // ログファイルパスの設定
            m_Log = new LogFile(System.IO.Directory.GetCurrentDirectory() + "\\" + LOGNAME);

            // 初期設定の読み込み
            if (LoadPrmFile() == false)
            {
                m_AllowAppExit = true;
                Close();
                return;
            }


            ////////////////////////////////////////
            // タイトル変更
            // タイトル変更のために必要な情報の取得（アセンブリ情報、実行ファイル情報）
            var asm = Assembly.GetExecutingAssembly();
            var fileInfo = new FileInfo(ExePath);

            // タイトル変更（実行ファイル更新日、バージョン情報を追加）
            this.Title = this.Title + string.Format(" Build:{0} Ver:{1}", fileInfo.LastWriteTime.ToShortDateString(), asm.GetName().Version);

            ////////////////////////////////////////
            // リストビューの初期化
            initDebugListView();

            ////////////////////////////////////////
            // ファイル転送クラスの初期化
            m_Trans = new TransFile((int)NET10_DIV.Net10Data + 1);
            m_Trans.OnFileTransError += new FileTransError(frmMain_OnFileTransError);
            m_Trans.OnFileTransComplete += new FileTransComplete(frmMain_OnFileTransComplete);

            ////////////////////////////////////////
            // パス情報の初期化
            ret = initFolderPath();
            if (ret == false)
            {
                m_AllowAppExit = true;
                Close();
                return;
            }

#if false   // 今回不要
            ////////////////////////////////////////
            // 浚渫ID、油回収IDをファイルから読み出す。
            m_IDMag = new IDManager();
            ret = m_IDMag.loadFile();
            if (ret == false)
            {
                MessageBox.Show(
                    "前回までの浚渫、油回収の回数が設定できません。" + NewLine + "ファイルを作成し、回数をデータベースから作成してください。",
                    "IDエラー", MessageBoxButton.OK, MessageBoxImage.Error);
                m_IDMag = null;
                m_AllowAppExit = true;
                Close();
                return;
            }
            else
            {
                m_DredgeID = m_IDMag.m_DredgeID;
                m_OilID = m_IDMag.m_OilID;

                lbDredgeID.Text = m_DredgeID.ToString();
                lbOilID.Text = m_OilID.ToString();
            }
#endif

            ////////////////////////////////////////
            // txtファイルの削除（前回の残骸）
            deleteGarbageFiles();

            ////////////////////////////////////////
            // 収録アドレスの初期化
            m_NET10Addr = new NET10Address();
            ret = m_NET10Addr.LoadAddress();
            if (ret == false)
            {
                MessageBox.Show("アドレスファイルの読み込みに失敗しました。",
                    "収録アドレスエラー", MessageBoxButton.OK, MessageBoxImage.Error);
                m_AllowAppExit = true;
                Close();
                return;
            }

            ////////////////////////////////////////
            // NET10通信の初期化
            m_NET10Ctrl = new NET10Control();
            m_NET10Ctrl.SetAddress(m_NET10Addr.m_AddrInfo.SAddr, m_NET10Addr.m_AddrInfo.DCnt, m_NET10Addr.m_AddrInfo.BlockCount);
            m_NET10Ctrl.OnArrivedData += new ArrivedData(frmMain_OnArrivedData);
            ret = m_NET10Ctrl.StartComm();

#if false
            if (ret == false)
            {
                MessageBox.Show("Net10通信ができません。プログラムは終了します。", "Net10データ収録", MessageBoxButton.OK, MessageBoxImage.Error);
                m_Log.SaveLog("Net10通信ができません。プログラムは終了します。");
                m_AllowAppExit = true;
                Close();
                return;
            }
#endif

            // 収録開始日時の表示
            txbStartTime.Text = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
            //setDebugListViewItem(2, 1, DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"));

            // ログ出力
            m_Log.SaveLog("データ収録開始");

            ////////////////////////////////////////
            // BD接続タイマーを設定
            timerUpdateUI = new DispatcherTimer(DispatcherPriority.Normal);
            timerUpdateUI.Interval = new TimeSpan(0, 0, 1);    //接続間隔
            timerUpdateUI.Tick += new EventHandler(timerUpdateUI_Tick);

            timerUpdateUI.Start();

            return;
        }

        /// <summary>
        /// 終了前処理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (WStartup) return;       // 二重起動のため、何もせずに終了

            // アプリの終了が許可されていない場合は、アプリを終了しない。
            //if ((e.CloseReason == CloseReason.UserClosing) && (m_AllowAppExit == false))
            if ((m_AllowAppExit == false))
            {
                MessageBox.Show("データ収録の終了には特別の操作が必要です。" + NewLine + "開発元に連絡して下さい。",
                "確認", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                e.Cancel = true;
            }
        }

        /// <summary>
        /// 終了処理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Closed(object sender, EventArgs e)
        {
            bool ret = false;

            // NET10通信の終了
            if (m_NET10Ctrl != null)
            {
                ret = m_NET10Ctrl.EndComm();
                if (ret == false)
                {
                    MessageBox.Show("チャネルクローズエラーです。", "Net10データ収録", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            m_Trans = null;

#if false   // 今回不要
            if (m_IDMag != null)
                m_IDMag.saveFile(m_IDMag.m_DredgeID, m_IDMag.m_OilID);
#endif
            m_Log.SaveLog("Net10toFILEプログラム終了");
        }

        //********************************************************************************
        // アプリケーションの終了を行う
        // このアプリは、フォームクローズではアプリが終了できず、動作状況アニメーションをダブルクリックする必要がある
        //********************************************************************************
        /// <summary>
        /// 動作状況アニメーションがダブルクリックされたときの終了処理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void toolStripStatusLabel4_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (e.ClickCount > 1)
            {
                CloseApplication();
                if (m_AllowAppExit)
                {
                    Environment.Exit(0);
                }

            }
        }

        private void toolStripStatusLabel5_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (e.ClickCount > 1)
            {
                CloseApplication();
                if (m_AllowAppExit)
                {
                    Environment.Exit(0);
                }
            }
        }

        public void CloseApplication()
        {
            MessageBoxResult ret;

            // 終了確認メッセージを表示
            ret = MessageBox.Show("データ収録を終了しますか？", "確認", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
            if (ret == MessageBoxResult.OK)
            {
                // OKを選択したらアプリ終了
                m_AllowAppExit = true;
                Close();
                return;
            }
        }

        //********************************************************************************
        // パラメータファイルの読み出し
        // True=正常,False=異常
        //********************************************************************************
        private bool LoadPrmFile()
        {
            string PrmFilePath = "";
            List<string> PrmData = null;

            // パラメータファイルの読み出し 
            PrmFilePath = Directory.GetCurrentDirectory() + @"\PrmFiles\System.ini";
            if (clsPrmFile.GetPrmData(PrmFilePath, ref PrmData) == true)
            {
                m_SourcePath = PrmData[0];
                m_DestPath = PrmData[1];
                m_IPAddress = PrmData[2];

                return true;
            }
            else
            {
                string exeName = Path.GetFileName(ExePath);
                // パラメータファイル読み出し不正
                MessageBox.Show(exeName + "のパラメータファイル読み出しに失敗しました。\r\n" +
                            "「" + PrmFilePath + "」を確認して下さい。", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        //********************************************************************************
        // 前回作成途中の収録ファイルを削除する
        //********************************************************************************
        public void deleteGarbageFiles()
        {
            try
            {
                string[] files;

                // 収録途中のファイル一覧の取得
                files = Directory.GetFiles(saveFolder[(int)SAVEPATH.保存先], "*" + TransFile.REC_FILE_EXT, System.IO.SearchOption.AllDirectories);

                // ファイル削除
                foreach (string file in files)
                    File.Delete(file);
            }
            catch (System.IO.DirectoryNotFoundException)
            {
            }
        }

        //********************************************************************************
        // リストビューの初期化
        //********************************************************************************
        public void initDebugListView()
        {
            txbFilePath.Text = "-";
            txbTransPath.Text = "-";
            txbStartTime.Text = "-";

            return;
        }

        //********************************************************************************
        // リストビューの内容変更
        //********************************************************************************
        //public void setDebugListViewItem(int row, int column, string text)
        //{
        //    DebugListView.Items[row].[column].Text = text;
        //}

        //********************************************************************************
        // ListView選択項目変更イベント
        //********************************************************************************
        //private void DebugListView_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        //{
        //    // 選択できないようにする
        //    if (e.IsSelected)
        //        e.Item.Selected = false;
        //}

        //********************************************************************************
        // フォルダパス設定の初期化
        //********************************************************************************
        public bool initFolderPath()
        {
            string str = "";

            // 保存先の読み出し
            saveFolder = new string[2];
            saveFolder[(int)SAVEPATH.保存先] = m_SourcePath + ROOTNAME[(int)SAVEPATH.保存先];
            saveFolder[(int)SAVEPATH.転送先] = m_DestPath + ROOTNAME[(int)SAVEPATH.転送先];

            // ドライブの存在確認（保存先のみ）
            for (int i = 0; i <= (int)SAVEPATH.保存先; i++)
            {
                string path = saveFolder[i].Substring(0, 3);
                if (Directory.Exists(path) == false)
                {
                    // 保存先ドライブが存在しない場合はエラー
                    MessageBox.Show(
                        "このコンピュータに設定された収録フォルダにアクセスできません。\r\n" + "収録フォルダ：" + m_SourcePath,
                        "収録フォルダーエラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }

            // ListViewに保存先、転送先を表示
            txbFilePath.Text = saveFolder[(int)SAVEPATH.保存先];
            txbTransPath.Text = saveFolder[(int)SAVEPATH.転送先];
            //setDebugListViewItem(0, 1, saveFolder[(int)SAVEPATH.保存先]);
            //setDebugListViewItem(1, 1, saveFolder[(int)SAVEPATH.転送先]);

            // 収録ファイルの保存ファイルパス（データ収録処理にてパスを生成）
            savePath = new string[(int)NET10_DIV.Net10Data + 1];

            for (int i = 0; i <= (int)NET10_DIV.Net10Data; i++)
            {
                // ファイル転送クラスに保存先、転送先フォルダパスを設定
                m_Trans.m_SrcDir[i] = saveFolder[(int)SAVEPATH.保存先] + "\\" + DIVNAME[i];
                m_Trans.m_DstDir[i] = saveFolder[(int)SAVEPATH.転送先] + "\\" + DIVNAME[i];
                savePath[i] = "";

                try
                {
                    // 収録フォルダの作成
                    str = m_Trans.m_SrcDir[i];
                    Directory.CreateDirectory(str);
                    str = m_Trans.m_DstDir[i];
                    Directory.CreateDirectory(str);
                }
                catch (Exception exp)
                {
                    MessageBox.Show("収録フォルダの作成に失敗しました。\r\n" + "収録フォルダ：" + str, "収録フォルダ作成エラー",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }

            return true;
        }

        //********************************************************************************
        // NET10データ受信イベント
        // NET10から読み出したデータを、ローカル(保存先)にファイル保存する（区分毎に保存フォルダが分かれている）
        // 一定数データを収録したら、別クラスに保存先フォルダ->転送先フォルダにファイル転送を依頼する
        //********************************************************************************
        private void frmMain_OnArrivedData(short[] rcvBuf)
        {
            bool ret = false;
            Stopwatch time = new Stopwatch();

            time.Restart(); // 処理時間の計測開始

            // 受信データのコピー
            m_RcvBuf = rcvBuf;

            // 保存日時の取得
            DateTime saveTime = DateTime.Now;

            // 前回と保存日時が同じであれば収録しない
            if (saveTime.ToString("yyyyMMddHHmmss") == m_SaveTimeOld.ToString("yyyyMMddHHmmss"))
                return;

            // 収録月が違う場合は、ファイル転送を実行させる
            if (saveTime.Month != m_SaveTimeOld.Month)
            {
                // 転送元ファイルが存在した時だけ処理を行う
                if (File.Exists(savePath[0]) != false)
                {
                    renameTransFile();  // 収録ファイル名を転送用に変更
                    execTransFile();    // ファイル転送実行
                }
            }

            // 受信データをファイルに保存する
            m_RecFileCount++;
            if (m_RecFileCount >= RecCountPerFile)
            {
                // 一定数(RecCountPerFile)収録した場合は、保存後ファイル拡張子を変更させる（転送ファイルは拡張子で判断するため）
                ret = saveNET10Data(true, saveTime);
                if (ret == true)
                {
                    execTransFile();    // ファイル転送実行
                }
                else
                {
                }
            }
            else
            {
                // ファイル保存のみ行う
                ret = saveNET10Data(false, saveTime);
            }

            if (ret == false)
            {
                m_RecFileCount--;
                m_TotalRecCount--;  // 既設アプリのソースままだが、この処理はいらない？
            }
            else
            {
                m_TotalRecCount++;
            }

            m_SaveTimeOld = saveTime;   // 前回の収録時間の格納

            time.Stop();     // 処理時間の計測停止
            m_RecTime = time.Elapsed;   // 処理時間の格納

            return;
        }

        //********************************************************************************
        // ファイル転送開始
        //********************************************************************************
        private void execTransFile()
        {
            // ファイル転送をしていない場合は、ファイル転送を開始する
            if (m_NowTrans == false)
            {
                // 転送前にPingを行い、接続先が存在するか確認
                if (Tools.sendPing(m_IPAddress, PING_TIMEOUT))
                {
                    m_TransError = false;   // 転送先アクセス異常を非表示

                    // 転送先フォルダの存在確認
                    if (Directory.Exists(saveFolder[(int)SAVEPATH.転送先]) == true)
                    {
                        // 転送開始
                        m_Trans.startFileTrans();
                        m_NowTrans = true;
                        m_TransState = 1;   // ステータスバーの表示更新
                    }
                    else
                    {
                        // 転送先が見つからない
                        m_TransState = 2;   // ステータスバーの表示更新
                    }
                }
                else
                {
                    m_TransError = true;   // 転送先アクセス異常を表示
                }
            }

            m_RecFileCount = 0; // 収録レコード数のリセット
        }

        //********************************************************************************
        // 収録ファイル転送エラーイベント
        //********************************************************************************
        private void frmMain_OnFileTransError(string error)
        {
            m_TransErrorCount++;    // 転送エラー回数のカウント（未使用）
            m_TransErrorOccurred = true;    // UI表示更新

            m_Log.SaveLog("転送エラー:" + error);

            m_NowTrans = false; // 本来、転送エラーが発生したら次回の転送からキャンセルする予定だったが、自動復旧を行うために追加した。
        }

        //********************************************************************************
        // 収録ファイル転送完了イベント
        //********************************************************************************
        private void frmMain_OnFileTransComplete(int count)
        {
            //m_ActualRecCount += RecCountPerFile;    // 収録レコード数のカウントアップ（既設アプリそのまま、* countしたほうが良い？)
            m_ActualRecCount += count;    // 収録レコード数のカウントアップ（TransFileで転送した収録レコード数をカウントするので加算）

            // 転送エラー情報のリセット
            m_TransErrorOccurred = false;
            m_TransErrorCount = 0;

            m_NowTrans = false; // 次の転送を許可
        }

        //********************************************************************************
        // NET10データをファイルに出力する。
        // isRename = trueの場合はファイル拡張子のリネームを行う（TransFileでファイルを転送させるため）
        //********************************************************************************
        private bool saveNET10Data(bool isRename, DateTime saveTime)
        {
            // RecIDの作成（収録データの管理ID）
            m_RecID = saveTime.Second;
            m_RecID += saveTime.Minute * 60;
            m_RecID += saveTime.Hour * 60 * 60;
            m_RecID += (saveTime.Day - 1) * 60 * 60 * 24;

            // ファイルに収録データを保存する
            string saveStr = "";

            for (int i = 0; i <= (int)NET10_DIV.Net10Data; i++)
            {
                // 収録ファイルパスの作成（ファイル拡張子のリネームの度、リセット）
                if (savePath[i] == "")
                {
                    savePath[i] = saveFolder[(int)SAVEPATH.保存先] + "\\" + DIVNAME[i] + "\\"
                        + DIVNAME[i] + saveTime.ToString("yyyyMMddHHmmss") + TransFile.REC_FILE_EXT;
                }

                // 保存ファイルのオープン
                using (StreamWriter file = new StreamWriter(savePath[i], true, new UTF8Encoding(false)))
                {
                    if (i == (int)NET10_DIV.DTMaster)
                    {
                        // DTMasterの場合
                        // 浚渫状態(W12F1 浚渫中（通常 0、浚渫中 1))
                        // SModeCheckCountはW12EFの位置を参照しているため、+2してW12F1を参照している
                        int SMode = m_RcvBuf[m_NET10Addr.SModeCheckCount + 2];  // 2005/03/22
                        if ((SMode) != 0)   // 全bit判定
                        {
                            // 浚渫IDを収録データに入れて、浚渫IDと経過時間をUI表示
                            if (m_TimeInfo[0].OnOff == false)
                            {
                                m_TimeInfo[0].OnOff = true; // UI表示フラグ
                                m_TimeInfo[0].startTime = DateTime.Now;     // 浚渫時間の開始時刻を更新
#if true
                                m_TimeInfo[0].idOld = 0;   // 収録するIDの変更
#else
                                // 今回不要
                                m_TimeInfo[0].idOld = m_IDMag.m_DredgeID;   // 収録するIDの変更

                                // 急にPCがダウンしたときのために、IDを保存する
                                m_IDMag.saveFile(m_IDMag.m_DredgeID, m_IDMag.m_OilID);

                                m_IDMag.m_DredgeID++;  // 次回のID
#endif
                            }
                        }
                        else
                        {
                            // 収録するIDを0にて、UI表示をやめる
                            if (m_TimeInfo[0].OnOff == true)
                            {
                                m_TimeInfo[0].OnOff = false; // UI表示フラグ
                                m_TimeInfo[0].idOld = 0;   // 収録するIDの変更
                            }
                        }

                        // 推進モード油回収自動(W0A03 3bit、但し手動操作ではONしない)
                        int OMode = m_RcvBuf[m_NET10Addr.OModeCheckCount];
                        if ((OMode & 0x08) != 0)
                        {
                            // 油回収IDを収録データに入れて、油回収IDと経過時間をUI表示
                            if (m_TimeInfo[1].OnOff == false)
                            {
                                m_TimeInfo[1].OnOff = true; // UI表示フラグ
                                m_TimeInfo[1].startTime = DateTime.Now;     // 油回収時間の開始時刻を更新
#if true
                                m_TimeInfo[0].idOld = 0;   // 収録するIDの変更
#else
                                // 今回不要
                                m_TimeInfo[1].idOld = m_IDMag.m_OilID;   // 収録するIDの変更

                                // 急にPCがダウンしたときのために、IDを保存する
                                m_IDMag.saveFile(m_IDMag.m_DredgeID, m_IDMag.m_OilID);

                                m_IDMag.m_OilID++;  // 次回のID
#endif
                            }
                        }
                        else
                        {
                            // 収録するIDを0にて、UI表示をやめる
                            if (m_TimeInfo[1].OnOff == true)
                            {
                                m_TimeInfo[1].OnOff = false; // UI表示フラグ
                                m_TimeInfo[1].idOld = 0;   // 収録するIDの変更
                            }
                        }
                        // 収録データの作成
                        saveStr = m_RecID.ToString() + "," + saveTime.ToString("yyyy-MM-dd HH:mm:ss.0000")
                            + "," + m_TimeInfo[0].idOld + "," + m_TimeInfo[1].idOld;
                    }
                    else
                    {
                        // DTMaster以外の収録データの作成
                        int startIdx = m_NET10Addr.m_AddrInfo.DelimitNo[i, 0];
                        int endIdx = m_NET10Addr.m_AddrInfo.DelimitNo[i, 1];

                        // 先頭はRecID
                        saveStr = m_RecID.ToString();

                        // 以降は各区分毎に指定されたアドレスのデータ
                        for (int j = startIdx; j <= endIdx; j++)
                            saveStr += "," + m_RcvBuf[j];
                    }

                    // ファイルの書込み
                    file.WriteLine(saveStr);
                }
            }

            // 呼び出し元より指示があった場合は、ファイル拡張子を変更する
            // 拡張子を変更することで、TransFileの転送ファイルの対象になる
            if (isRename == true)
                renameTransFile();  // 収録ファイル名を転送用に変更

            return true;
        }

        //********************************************************************************
        // ファイルを転送するために拡張子を変更する
        //********************************************************************************
        public void renameTransFile()
        {
            // 転送元ファイルが存在しなかったら何もしない
            if (File.Exists(savePath[0]) == false)
                return;

            string fileExt;

            if (checkRecFileDate(savePath[0]) == true)
            {
                fileExt = TransFile.TRANS_FILE_EXT; // 転送ファイルの拡張子
            }
            else
            {
                fileExt = TransFile.ERROR_FILE_EXT; // 収録データにエラーがあったファイルの拡張子
            }

            for (int i = 0; i <= (int)NET10_DIV.Net10Data; i++)
            {
                int offset = savePath[i].LastIndexOf('.');

                string tmpStr = savePath[i].Substring(0, offset);

                FileInfo info = new FileInfo(savePath[i]);
                info.MoveTo(tmpStr + fileExt);
                savePath[i] = "";
            }
        }

        //********************************************************************************
        // 収録データファイルの日付チェック
        //********************************************************************************
        public bool checkRecFileDate(string path)
        {
            try
            {
                bool isFirst = false;
                string[] readStr = new string[2];
                string[] splitStr1, splitStr2;
                int[] month = new int[2];

                // 収録データファイルの最初と最後の行を読み込み
                using (StreamReader file = new StreamReader(path, Encoding.UTF8))
                {
                    while (file.Peek() >= 0)
                    {
                        if (isFirst == false)
                        {
                            isFirst = true;
                            readStr[0] = file.ReadLine();
                        }
                        else
                        {
                            readStr[1] = file.ReadLine();
                        }
                    }
                }

                // 収録レコードの月部分を取得
                splitStr1 = readStr[0].Split(',');
                splitStr2 = readStr[1].Split(',');

                month[0] = ToInt32(splitStr1[1].Substring(5, 2));
                month[1] = ToInt32(splitStr2[1].Substring(5, 2));

                // 月が違う場合はファイルを破棄する
                if (month[0] != month[1])
                    return false;
            }
            catch
            {
                return false;
            }

            return true;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            // Storyboard開始
            mStoryboard.Begin(this);
        }

        //********************************************************************************
        // UI更新
        //********************************************************************************
        private void timerUpdateUI_Tick(object sender, EventArgs e)
        {
            ////////////////////////////////////////
            if (m_TransError)
                lbTransErr.Visibility = Visibility.Visible; // 転送先アクセス異常
            else
                lbTransErr.Visibility = Visibility.Hidden;  // エラー非表示

            // RecIDの表示更新
            lbRecID.Text = m_RecID.ToString();

            // 起動時からの転送レコード数を表示（アプリ終了までリセットされない）
            lbTotalActual.Text = m_ActualRecCount.ToString();

            // 起動時からの収録レコード数を表示（アプリ終了までリセットされない）
            lbTotal.Text = m_TotalRecCount.ToString();

#if false   // 今回不要
            ////////////////////////////////////////
            // SID（浚渫ID）の表示更新
            if (m_TimeInfo[0].OnOff == true)
            {
                lbDredgeID.Text = m_TimeInfo[0].idOld.ToString();
                lbDredgeTime.Text = (DateTime.Now - m_TimeInfo[0].startTime).ToString("hh\\:mm\\:ss");  // 浚渫時間（秒）

                lbDredgeIDTitle.Foreground = new SolidColorBrush(Colors.Black);
                lbDredgeIDTitle.Background = new SolidColorBrush(Colors.Yellow);
            }
            else
            {
                lbDredgeIDTitle.Foreground = new SolidColorBrush(Colors.White);
                lbDredgeIDTitle.Background = new SolidColorBrush(Colors.Transparent);
            }

            // OID（油回収ID）の表示更新
            if (m_TimeInfo[1].OnOff == true)
            {
                lbOilID.Text = m_TimeInfo[1].idOld.ToString();
                lbOilTime.Text = (DateTime.Now - m_TimeInfo[1].startTime).ToString("hh\\:mm\\:ss"); // 油回収時間

                lbOilIDTitle.Foreground = new SolidColorBrush(Colors.Black);
                lbOilIDTitle.Background = new SolidColorBrush(Colors.Yellow);
            }
            else
            {
                lbOilIDTitle.Foreground = new SolidColorBrush(Colors.White);
                lbOilIDTitle.Background = new SolidColorBrush(Colors.Transparent);
            }
#endif

            ////////////////////////////////////////
            // 収録にかかった時間
            txtRecTime.Text = m_RecTime.ToString("s\\.fff");

            // 収録データ数（ファイルに保存したレコード数）
            if (m_RecFileCount <= 0)
                txtRecFileCount.Text = RecCountPerFile.ToString();
            else
                txtRecFileCount.Text = m_RecFileCount.ToString();

            ////////////////////////////////////////
            // ステータスバー表示
            // アプリの状態表示
            string str = "";
            if (m_TransState == 0)
                str = "Net10データ収録が開始されました。";
            else if (m_TransState == 1)
                str = "正常動作中...";
            else if (m_TransState == 2)
                str = "転送中止。" + saveFolder[(int)SAVEPATH.転送先] + ":未確認";

            toolStripStatusLabel1.Text = str;

            // ファイル転送エラーの有無を表示
            if (m_TransErrorOccurred == true)
                toolStripStatusLabel2.Text = "転送エラー。ログ参照";
            else
                toolStripStatusLabel2.Text = "";

            // 現在時刻の表示
            toolStripStatusLabel3.Text = DateTime.Now.ToShortTimeString();

            // ファイル転送中である事が分かるよう画像表示
            if (!m_NowTrans)
            {
                // アニメーション表示
                toolStripStatusLabel4.Visibility = Visibility.Visible;
                toolStripStatusLabel5.Visibility = Visibility.Hidden;
            }
            else
            {
                // アニメーション非表示
                toolStripStatusLabel4.Visibility = Visibility.Hidden;
                toolStripStatusLabel5.Visibility = Visibility.Visible;
            }
            //if (m_NowTrans == true)
            //    toolStripStatusLabel4. = imageList1.Images[1];
            //else
            //    toolStripStatusLabel4.Image = imageList1.Images[0];

            return;
        }

        /// <summary>
        /// メッセージ キューに現在ある Windows メッセージをすべて処理する(WPFでのDoEvents)
        /// </summary>
        public void DoEvents()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new DispatcherOperationCallback(ExitFrames), frame);
            Dispatcher.PushFrame(frame);
        }

        public object ExitFrames(object f)
        {
            ((DispatcherFrame)f).Continue = false;

            return null;
        }

    }
}
