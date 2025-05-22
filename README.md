# HIDAutoBrightnessAdjuster
AutoBrightnessAdjuster for Windows By ChatGPT

It's for my Macbook Pro with Boot camp, which is unable to change brightness automatically.

It may also suitable for your PC with HID light sensor.

Most of code was made by ChatGPT, because i dont know C#. 

## 使用方法 Usage method

送AutoBrightnessAdjuster_NoConsole.vbs的快捷方式到路径"shell:startup"可开机启动。

Send the shortcut of AutoBrightnessAdjuster.vbs to the path "shell:startup" to start automatically at startup.

在任务管理器中结束任务来关闭功能。

End the task in Task Manager to close the feature.

## 特性 Feature
1.若检测到环境亮度变化小于5lux，不调整亮度，等待5秒后再检测。
If the change in ambient brightness is less than 5 lux, do not adjust the brightness, and wait for 5 seconds before checking again.

2.如果检测到亮度手动调整，跳过本次循环。
If manual brightness adjustment detected, Skipping this cycle.

3.lux变化小于2lux，不改变亮度。
If the change in lux is less than 2 lux, do not change the brightness.

## Copyright
未经允许，请勿转载。经允许的转载请注明来源。

Without permission, do not reprint. Reprints with permission must indicate the source.
