// Still Here: gentle Taptic Engine feedback for the iOS app. Called from Haptics.cs.
#import <UIKit/UIKit.h>

static void SHImpact(UIImpactFeedbackStyle style, CGFloat intensity)
{
    UIImpactFeedbackGenerator *g = [[UIImpactFeedbackGenerator alloc] initWithStyle:style];
    [g prepare];
    [g impactOccurredWithIntensity:intensity];
}

extern "C" void SH_iOSHaptic(int kind)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        switch (kind)
        {
            case 0: SHImpact(UIImpactFeedbackStyleLight, 0.45); break;   // tick
            case 1: SHImpact(UIImpactFeedbackStyleSoft, 0.7); break;     // soft: wood, fabric, walls
            case 2: SHImpact(UIImpactFeedbackStyleRigid, 0.6); break;    // firm: metal, glass, china
            case 3: SHImpact(UIImpactFeedbackStyleMedium, 0.85); break;  // knock: walking into something
            case 4:                                                      // heavy: stumble or fall
                SHImpact(UIImpactFeedbackStyleHeavy, 1.0);
                dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.11 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{ SHImpact(UIImpactFeedbackStyleHeavy, 0.7); });
                break;
            case 5:                                                      // purr: a cat against your legs
                for (int i = 0; i < 6; i++)
                    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(i * 0.045 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{ SHImpact(UIImpactFeedbackStyleSoft, 0.35); });
                break;
            default:                                                     // burn
            {
                UINotificationFeedbackGenerator *n = [[UINotificationFeedbackGenerator alloc] init];
                [n notificationOccurred:UINotificationFeedbackTypeError];
            }
        }
    });
}
