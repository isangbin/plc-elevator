using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XGCommLibDemo; // XGCommSocket 클래스가 포함된 네임스페이스

namespace Elevator
{
    public partial class MainWindow : Window
    {
        // 1. 통신 객체 선언 [cite: 789]
        private XGCommSocket plc = new XGCommSocket();

        private string _plcIp = "192.168.1.200"; // 실물 PLC IP [cite: 134, 436]
        private ushort _plcPort = 2004;          // 이더넷 기본 포트 [cite: 80, 586]
        private bool _isConnected = false;

        public MainWindow()
        {
            InitializeComponent();

            // 2. 프로그램 시작 시 PLC 연결 [cite: 7, 181]
            uint ret = plc.Connect(_plcIp, _plcPort);

            if (ret == (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS)
            {
                _isConnected = true;
                // 연결 성공 시 하단 텍스트나 로그에 표시 (선택 사항)
                MessageBox.Show("PLC Connected.");
            }
            else
            {
                MessageBox.Show("PLC 연결 실패: " + plc.GetReturnCodeString(ret));
            }
        }

        // [외부 버튼 클릭] 1U, 2U, 2D 등
        private void CallButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected) return;

            if (sender is Button btn && btn.Tag != null)
            {
                string tag = btn.Tag.ToString();
                int addr = GetOuterAddr(tag);

                if (addr != -1)
                {
                    // 3. PLC 메모리를 1로 변경 (%MX 주소 사용) [cite: 487, 810]
                    byte[] data = { 1 };
                    MessageBox.Show("M" + ((uint)addr).ToString() + data);
                    plc.WriteDataBit('M', (uint)addr, 1, data);
                    MessageBox.Show("dd");

                    // UI 표시: 버튼을 주황색으로 변경
                    btn.Background = Brushes.Orange;
                }
            }
        }

        // [내부 버튼 클릭] 1, 2, 3, 4, Open, Close 등
        private void InnerButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected) return;

            if (sender is Button btn && btn.Tag != null)
            {
                string tag = btn.Tag.ToString();
                int addr = GetInnerAddr(tag);

                if (addr != -1)
                {
                    // 3. PLC 메모리를 1로 변경 [cite: 810, 824]
                    byte[] data = { 1 };
                    plc.WriteDataBit('M', (uint)addr, 1, data);
                }
            }
        }

        // 주소 매핑: Tag 이름을 %MX 오프셋 숫자로 변환 [cite: 487, 505, 520]
        private int GetOuterAddr(string tag)
        {
            switch (tag)
            {
                case "1U": return 10; // %MX10
                case "2U": return 20; // %MX20
                case "2D": return 21; // %MX21
                case "3U": return 30; // %MX30
                case "3D": return 31; // %MX31
                case "4D": return 41; // %MX41
                default: return -1;
            }
        }

        private int GetInnerAddr(string tag)
        {
            switch (tag)
            {
                case "1": return 110;     // %MX110
                case "2": return 120;     // %MX120
                case "3": return 130;     // %MX130
                case "4": return 140;     // %MX140
                case "Open": return 100;  // %MX100
                case "Close": return 101; // %MX101
                default: return -1;
            }
        }

        // 프로그램 종료 시 연결 해제 [cite: 853]
        protected override void OnClosed(EventArgs e)
        {
            if (_isConnected)
            {
                plc.Disconnect();
            }
            base.OnClosed(e);
        }
    }
}