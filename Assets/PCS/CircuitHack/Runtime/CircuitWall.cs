using UnityEngine;
using UnityEngine.UI;
namespace FourPaws.PCS.CircuitHack
{
    // Walls 안에서 이 표식이 활성인 직사각형만 충돌 벽이다. 장식에는 붙이지 않는다.
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform), typeof(Image))]
    public sealed class CircuitWall : MonoBehaviour { }
}
