using System;
using System.Collections.Generic;
using System.Threading.Tasks; // Task 사용을 위해 추가
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation; // 애니메이션 사용을 위해 추가
using System.Windows.Threading; // 타이머 사용을 위해 추가
using XGCommLibDemo; // XGCommSocket 클래스가 포함된 네임스페이스

namespace Elevator
{
    public partial class MainWindow : Window
    {
        // 1. 통신 객체 선언
        private XGCommSocket plc = new XGCommSocket();

        private string _plcIp = "192.168.1.200"; // 실물 PLC IP
        private ushort _plcPort = 2004;          // 이더넷 기본 포트
        private bool _isConnected = false;

        private DispatcherTimer pollTimer;
        private bool _isProcessing = false; // 동작 중 중복 실행 방지
        private readonly Dictionary<string, double> _floorPositions = new Dictionary<string, double>
        {
            { "1F", 72 }, { "2F", 210 }, { "3F", 348 }, { "4F", 486 }
        };


        public MainWindow()
        {
            InitializeComponent();

            // 2. 프로그램 시작 시 PLC 연결
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

            // --- 타이머 초기화 ---
            pollTimer = new DispatcherTimer();
            pollTimer.Interval = TimeSpan.FromMilliseconds(500); // 0.5초마다 PLC 상태 확인
            pollTimer.Tick += PollElevatorStatus;
            pollTimer.Start();
        }

        // --- 실시간 상태 감시 함수 ---
        private void PollElevatorStatus(object sender, EventArgs e)
        {
            if (!_isConnected || _isProcessing) return;

            // 각 층 신호(%MX10, 20, 30, 40) 확인
             CheckAndRun(1010, "1F");
             CheckAndRun(1020, "2F");
             CheckAndRun(1021, "2F");
             CheckAndRun(1030, "3F");
             CheckAndRun(1031, "3F");
             CheckAndRun(1041, "4F");
             CheckAndRun(1110, "1F");
             CheckAndRun(1120, "2F");
             CheckAndRun(1130, "3F");
             CheckAndRun(1140, "4F");
        }

        private void CheckAndRun(uint addr, string floorName)
        {
            byte[] status = new byte[1];
            if (plc.ReadDataBit('M', addr, 1, status) == (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS)
            {
                if (status[0] == 1) // PLC 메모리 값이 1이면 시퀀스 시작
                {
                    _isProcessing = true;
                    ExecuteSequence(floorName);
                }
            }
        }

        // --- 이동 및 문 개폐 시퀀스 추가 ---
        private async void ExecuteSequence(string floorName)
        {
            // 1. 엘리베이터 이동
            double targetPos = _floorPositions[floorName];
            DoubleAnimation moveAnim = new DoubleAnimation(targetPos, TimeSpan.FromSeconds(2)); // 2초간 이동
            moveAnim.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            ElevatorCar.BeginAnimation(Canvas.BottomProperty, moveAnim);

            await Task.Delay(2000); // 이동 시간 대기
            TxtSmallFloor.Text = floorName;
            Text_InnerFloor.Text = floorName;

            // 2. 문 열기
            ControlDoors(-100, 100);
            await Task.Delay(1000); // 문 열리는 시간

            // 3. 2초간 열림 유지
            await Task.Delay(2000);

            // 4. 문 닫기
            ControlDoors(0, 0);
            await Task.Delay(1000); // 문 닫히는 시간

            _isProcessing = false; // 시퀀스 종료
        }

        private void ControlDoors(double leftTo, double rightTo)
        {
            DoubleAnimation leftAnim = new DoubleAnimation(leftTo, TimeSpan.FromSeconds(1));
            DoubleAnimation rightAnim = new DoubleAnimation(rightTo, TimeSpan.FromSeconds(1));

            // XAML에 정의된 Transform 이름에 연결
            LeftDoorTransform.BeginAnimation(TranslateTransform.XProperty, leftAnim);
            RightDoorTransform.BeginAnimation(TranslateTransform.XProperty, rightAnim);
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
                    // 3. PLC 메모리를 1로 변경 (%MX 주소 사용)
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
                    // 3. PLC 메모리를 1로 변경
                    byte[] data = { 1 };
                    plc.WriteDataBit('M', (uint)addr, 1, data);
                }
            }
        }

        // 주소 매핑: Tag 이름을 %MX 오프셋 숫자로 변환
        private int GetOuterAddr(string tag)
        {
            switch (tag)
            {
                 case "1U": return 1010; // %MX10
                 case "2U": return 1020; // %MX20
                 case "2D": return 1021; // %MX21
                 case "3U": return 1030; // %MX30
                 case "3D": return 1031; // %MX31
                 case "4D": return 1041; // %MX41
                default: return -1;
            }
        }

        private int GetInnerAddr(string tag)
        {
            switch (tag)
            {
                 case "1": return 1110;     // %MX110
                 case "2": return 1120;     // %MX120
                 case "3": return 1130;     // %MX130
                 case "4": return 1140;     // %MX140
                 case "Open": return 1100;  // %MX100       아직로직없음
                 case "Close": return 1101; // %MX101
                default: return -1;
            }
        }

        // 프로그램 종료 시 연결 해제
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