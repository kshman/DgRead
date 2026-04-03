using Avalonia.Media.Imaging;

namespace DgRead.Chaek;

/// <summary>
/// 애니메이션 프레임 정보입니다.
/// </summary>
/// <param name="Bitmap">프레임 비트맵입니다.</param>
/// <param name="Duration">프레임 표시 시간(밀리초)입니다.</param>
public sealed record AnimatedFrame(Bitmap Bitmap, int Duration);
