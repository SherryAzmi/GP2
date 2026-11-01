using UnityEngine;

/// <summary>
/// Put this on a realistic character prefab so BeachLife can use it in place of the built-in
/// placeholder figures. Every field is optional:
///   - With limb Transforms assigned, BeachLife swings them procedurally (walking, throwing, digging...).
///   - With an Animator assigned, BeachLife instead cross-fades to a state named after the activity
///     (Walk, Throw, Splash, Wave, Sunbathe, Sit, BuildCastle) when that state exists.
///   - With neither, the character is simply moved around and bobbed.
/// </summary>
public class BeachPersonRig : MonoBehaviour
{
    public Animator animator;
    public Transform armLeft, armRight, legLeft, legRight;
    [Tooltip("Height of the model's hips above the ground when standing; used to lower it into a sitting pose")]
    public float hipHeight = 0.9f;
}
