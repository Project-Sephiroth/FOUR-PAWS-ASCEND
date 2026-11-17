using UnityEngine;

public sealed class BearCarryPresentation : MonoBehaviour
{
    [SerializeField] private SpriteRenderer normalModel;
    [SerializeField] private SpriteRenderer heldPose;
    [SerializeField] private Animator heldAnimator;
    [SerializeField] private AnimationClip rockPose;
    [SerializeField] private AnimationClip objectPose;
    [SerializeField] private AnimationClip frogPose;
    [SerializeField] private AnimationClip mousePose;
    [SerializeField] private AnimationClip rabbitPose;
    [SerializeField] private SpriteRenderer heldAlly;
    [SerializeField] private Animator allyAnimator;
    private AnimationClip currentAlly;
    private AnimationClip current;

    public bool HasAllyPose(MyEnum.CharacterType character) => GetAllyPose(character) != null;

    private AnimationClip GetAllyPose(MyEnum.CharacterType character) => character switch
    {
        MyEnum.CharacterType.Frog => frogPose,
        MyEnum.CharacterType.Mouse => mousePose,
        MyEnum.CharacterType.Rabbit => rabbitPose,
        _ => null
    };

    public void Show(bool holdingObject, bool holdingRock, MyEnum.CharacterType ally, bool facesLeft)
    {
        AnimationClip objectClip = !holdingObject ? null : holdingRock ? rockPose : objectPose;
        ShowPose(heldPose, heldAnimator, objectClip, facesLeft, ref current);
        ShowPose(heldAlly, allyAnimator, GetAllyPose(ally), facesLeft, ref currentAlly);
        normalModel.enabled = objectClip == null;
    }

    private void ShowPose(SpriteRenderer model, Animator animator, AnimationClip clip, bool facesLeft, ref AnimationClip previous)
    {
        bool visible = clip != null;
        if (previous != clip || model.gameObject.activeSelf != visible)
        {
            previous = clip;
            model.gameObject.SetActive(visible);
            if (visible)
            {
                animator.Play("Base Layer." + clip.name, 0, 0f);
                animator.Update(0f);
            }
        }

        model.flipX = facesLeft;
        model.color = normalModel.color;
    }

    private void OnDisable()
    {
        if (normalModel != null)
            normalModel.enabled = true;
        if (heldPose != null)
            heldPose.gameObject.SetActive(false);
        if (heldAlly != null)
            heldAlly.gameObject.SetActive(false);
    }
}
