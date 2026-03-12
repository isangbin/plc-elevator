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
        private readonly Dictionary<int, double> _floorPositions = new Dictionary<int, double>
        {
            { 1, 72 }, { 2, 210 }, { 3, 348 }, { 4, 486 }
        };

        private int _lastTargetFloor = -1; // 현재 목표 층을 기억하여 중복 애니메이션 방지
        private int _lastDoorState = -1;   // 이전 문 상태 기억 (%MW112)

        public MainWindow()
        {
            InitializeComponent();

            // 2. 프로그램 시작 시 PLC 연결
            uint ret = plc.Connect(_plcIp, _plcPort);

            if (ret == (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS)
            {
                _isConnected = true;
                // 연결 성공 시
                MessageBox.Show("PLC Connected.");
            }
            else
            {   // 연결 실패 시
                MessageBox.Show("PLC 연결 실패: " + plc.GetReturnCodeString(ret));
            }

            // --- 타이머 초기화 ---
            pollTimer = new DispatcherTimer();
            pollTimer.Interval = TimeSpan.FromMilliseconds(500); // 0.5초마다 PLC 상태 확인
            // pollTimer.Tick += PollElevatorStatus;
            pollTimer.Tick += PollPlcStatus;
            pollTimer.Start();
        }

        private void PollPlcStatus(object sender, EventArgs e)
        {
            if (!_isConnected) return;

            // 1. 엘리베이터 위치 감시 (%MW110)
            ushort[] posData = new ushort[1];
            
            if (plc.ReadDataWord('M', 110, 1, false, posData) == (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS)
            {
                int floorValue = (int)posData[0];

                // 목표 층이 실제로 바뀌었을 때만 애니메이션 실행 (애니메이션 느려짐 방지)
                if (floorValue != _lastTargetFloor && _floorPositions.ContainsKey(floorValue))
                {
                    _lastTargetFloor = floorValue; // 새로운 목표 층 저장
                    UpdateElevatorPosition(_floorPositions[floorValue], floorValue);
                }
            }

            // 2. 문 상태 감시 (%MW112)
            ushort[] doorData = new ushort[1];
            if (plc.ReadDataWord('M', 112, 1, false, doorData) == (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS)
            {
                int currentState = (int)doorData[0];

                // 방향 표시 업데이트 (%MW112 값에 따라 ▲, ▼, 빈칸 결정)
                UpdateDirectionIndicator(currentState);

                // 문 상태값이 변했을 때만 문 애니메이션 실행 (느려짐 방지)
                if (currentState != _lastDoorState)
                {
                    _lastDoorState = currentState;
                    UpdateDoorState(currentState);
                }
            }

            // 3. 버튼 램프 상태 감시
            UpdateButtonLamp();
        }

        // 버튼이 눌려있는지 아닌지 감시하는 함수
        private void UpdateButtonLamp()
        {
            // %MX300부터 %MX322까지 포함할 수 있도록 23개 비트를 한 번에 읽음 (통신 1회로 단축)
            byte[] lampStatus = new byte[23];

            // ReadDataBit(타입, 시작주소, 읽을 개수, 결과배열)
            if (plc.ReadDataBit('M', 300, 23, lampStatus) == (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS)
            {
                // 1. 내부 버튼 (%MX300 ~ 303)
                // 인덱스 계산: 목표주소 - 시작주소(300)
                SetColorFromBuffer(Inner_1, lampStatus[0]); // 300 - 300 = 0
                SetColorFromBuffer(Inner_2, lampStatus[1]); // 301 - 300 = 1
                SetColorFromBuffer(Inner_3, lampStatus[2]); // 302 - 300 = 2
                SetColorFromBuffer(Inner_4, lampStatus[3]); // 303 - 300 = 3

                // 2. 외부 버튼 (%MX310 ~ 322)
                // 인덱스 계산: 목표주소 - 시작주소(300)
                SetColorFromBuffer(Outer_1U, lampStatus[10]); // 310 - 300 = 10
                SetColorFromBuffer(Outer_2U, lampStatus[11]); // 311 - 300 = 11
                SetColorFromBuffer(Outer_3U, lampStatus[12]); // 312 - 300 = 12

                SetColorFromBuffer(Outer_2D, lampStatus[20]); // 320 - 300 = 20
                SetColorFromBuffer(Outer_3D, lampStatus[21]); // 321 - 300 = 21
                SetColorFromBuffer(Outer_4D, lampStatus[22]); // 322 - 300 = 22
            }
        }

        // 버튼 눌려있음에 따른 컬러 변화
        private void SetColorFromBuffer(Button btn, byte state)
        {
            if (btn == null) return;

            // 현재 색상 확인 (불필요한 UI 소모 방지)
            Brush targetColor = (state == 1) ? Brushes.Orange : new SolidColorBrush(Color.FromRgb(45, 48, 53));

            // 색상이 다를 때만 업데이트 (렉 방지)
            if (btn.Background.ToString() != targetColor.ToString())
            {
                btn.Background = targetColor;
            }
        }

        // 엘리베이터 내부 스크린 텍스트
        private void UpdateDirectionIndicator(int state)
        {
            // 1이면 위(▲), 2면 아래(▼), 그 외에는 빈칸("")
            switch (state)
            {
                case 1:
                    Text_InnerDir.Text = "▲";
                    break;
                case 2:
                    Text_InnerDir.Text = "▼";
                    break;
                default:
                    Text_InnerDir.Text = ""; // 0, 3, 4, 5, 6은 대기 또는 문 동작 중이므로 빈칸
                    break;
            }
        }

        // 엘리베이터 위치 업데이트
        private void UpdateElevatorPosition(double targetBottom, int floor)
        {
            // 여기서는 상시 주시이므로 부드러운 이동(2초)으로 설정
            // DoubleAnimation anim = new DoubleAnimation(targetBottom, TimeSpan.FromMilliseconds(2000));
            DoubleAnimation anim = new DoubleAnimation
            {
                To = targetBottom,
                Duration = TimeSpan.FromMilliseconds(2000), // 0.5초 동안 이동
                                                            // EasingFunction으로 가감속 없는 등속 운동 설정
                EasingFunction = new PowerEase { Power = 1, EasingMode = EasingMode.EaseInOut }
            };
            ElevatorCar.BeginAnimation(Canvas.BottomProperty, anim);

            // 스크린 및 Car에 현재 층 표시
            TxtSmallFloor.Text = floor + "F";
            Text_InnerFloor.Text = floor + "F";
        }

        // 문 상태 업데이트 (%MW112 값에 따른 분기)
        private void UpdateDoorState(int state)
        {
            switch (state)
            {
                case 4: // 2초에 걸쳐 문 열림
                    AnimateDoors(-100, 100, 1.5);
                    break;
                case 5: // 문 다 열린 상태로 정지 (애니메이션 없이 좌표 고정)
                    StopDoorAnimation(-100, 100);
                    break;
                case 6: // 2초에 걸쳐 문 닫힘
                    AnimateDoors(0, 0, 1.5);
                    break;
            }
        }

        // 문 움직임 함수 (열기, 닫기)
        private void AnimateDoors(double leftTo, double rightTo, double seconds)
        {
            DoubleAnimation leftAnim = new DoubleAnimation(leftTo, TimeSpan.FromSeconds(seconds));
            DoubleAnimation rightAnim = new DoubleAnimation(rightTo, TimeSpan.FromSeconds(seconds));

            LeftDoorTransform.BeginAnimation(TranslateTransform.XProperty, leftAnim);
            RightDoorTransform.BeginAnimation(TranslateTransform.XProperty, rightAnim);
        }

        // 문 열린채로 고정하는 함수
        private void StopDoorAnimation(double leftPos, double rightPos)
        {
            // 애니메이션을 중단하고 해당 위치에 고정
            LeftDoorTransform.BeginAnimation(TranslateTransform.XProperty, null);
            RightDoorTransform.BeginAnimation(TranslateTransform.XProperty, null);
            LeftDoorTransform.X = leftPos;
            RightDoorTransform.X = rightPos;
        }

        /*
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
        */

        // [외부 버튼 클릭] 1U, 2U, 2D 등
        private async void CallButton_Click(object sender, RoutedEventArgs e)
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
                    byte[] data0 = { 0 };
                    // MessageBox.Show("메모리주소 디버깅용");
                    plc.WriteDataBit('M', (uint)addr, 1, data);
                    // MessageBox.Show("메모리쓰기 디버깅용");
                    await Task.Delay(200);
                    plc.WriteDataBit('M', (uint)addr, 1, data0);


                    // UI 표시: 버튼을 주황색으로 변경
                    // btn.Background = Brushes.Orange;
                }
            }
        }

        // [내부 버튼 클릭] 1, 2, 3, 4, Open, Close 등
        private async void InnerButton_Click(object sender, RoutedEventArgs e)
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
                    byte[] data0 = { 0 };
                    plc.WriteDataBit('M', (uint)addr, 1, data);
                    await Task.Delay(200);
                    plc.WriteDataBit('M', (uint)addr, 1, data0);
                }
            }
        }

        // 주소 매핑: Tag 이름을 %MX 오프셋 숫자로 변환
        private int GetOuterAddr(string tag)
        {
            switch (tag)
            {
                 case "1U": return 10; // %MX10
                 case "2U": return 11; // %MX11
                 case "2D": return 12; // %MX12
                 case "3U": return 13; // %MX13
                 case "3D": return 14; // %MX14
                 case "4D": return 16; // %MX16
                default: return -1;
            }
        }

        private int GetInnerAddr(string tag)
        {
            switch (tag)
            {
                 case "1": return 0;     // %MX0
                 case "2": return 1;     // %MX1
                 case "3": return 2;     // %MX2
                 case "4": return 3;     // %MX3
                 case "Open": return 20;  // %MX20
                 case "Close": return 21; // %MX21
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