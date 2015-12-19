MLFLAGS = /c /coff /Cp /Fl /Sc /Sg

All: Invoke.dll

Clean: 
	-@if exist Invoke.obj erase Invoke.obj
	-@if exist Invoke.dll erase Invoke.dll
	-@if exist Invoke.lib erase Invoke.lib
	-@if exist Invoke.exp erase Invoke.exp
	-@if exist Invoke.lst erase Invoke.lst

Invoke.obj: Invoke.asm
        ml $(MLFLAGS) $**


Invoke.dll: Invoke.obj
	link $** -DLL -entry:DllMain /machine:i386 /subsystem:windows /out:Invoke.dll /export:InvokeFunc,@1 /export:InvokePluginInit,@2
