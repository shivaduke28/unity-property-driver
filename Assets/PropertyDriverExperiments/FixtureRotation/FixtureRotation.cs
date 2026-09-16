using UnityEngine;

namespace PropertyDriverExperiments
{
    /// <summary>
    /// 慣性のあるハード（ムービングライトなど）の回転を模したコンポーネント。
    /// <see cref="SetTargetRotation"/> で信号を受け取り、LateUpdate で速度制限つきにターゲットへ寄せる。
    /// ターゲットが無ければ何もしない。ExecuteAlways なので編集モードでもエディタのフレームごとに動く
    /// （編集モードでも Time.deltaTime には実フレーム差分が入り、Time.maximumDeltaTime で頭打ちになることを実測済み）。
    /// Transform の driven 登録や復元はここでは扱わない。プレビューで一時的に動かす側（Timeline トラック）の責務。
    /// </summary>
    [ExecuteAlways]
    public class FixtureRotation : MonoBehaviour
    {
        [SerializeField, Min(0f)] float degreesPerSecond = 90f;

        Quaternion? targetRotation;

        /// <summary>現在の信号。無ければ null。</summary>
        public Quaternion? TargetRotation => targetRotation;

        /// <summary>信号の受信。</summary>
        public void SetTargetRotation(Quaternion rotation) => targetRotation = rotation;

        /// <summary>信号の途絶。以降 LateUpdate は何もしない。</summary>
        public void ClearTarget() => targetRotation = null;

        void LateUpdate()
        {
            if (targetRotation is not { } target) return;
            transform.localRotation = Quaternion.RotateTowards(transform.localRotation, target, degreesPerSecond * Time.deltaTime);
        }
    }
}
