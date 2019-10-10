rem SetLocale "ru"
Const ForReading = 1, ForWriting = 2, ForAppending = 8

Set oFSO = CreateObject("Scripting.FileSystemObject")
rem Set fLog = oFSO.OpenTextFile("log.txt", ForWriting, True)
rem MsgBox
Set NameToIDDictionary = CreateObject("Scripting.Dictionary")

LoadXmlTV NameToIDDictionary

Const FilePath = "index.m3u"
Const newFile = "notfound.m3u"

Set fFrom = oFSO.OpenTextFile(FilePath, ForReading, False)
Set fTo = oFSO.OpenTextFile(newFile, ForWriting, True)

Do While fFrom.AtEndOfStream <> True
	displayName = ""
	inline = fFrom.ReadLine
	If "#" = Left(inline, 1) Then
		ndx = InStr(inline, ",")
		If ndx > 0 Then
			displayName = Mid(inline, ndx + 1)
			displayName = DecodeUTF8(displayName)
			displayName = Trim(displayName)
			displayName = LCase(displayName)
		End If

		If Not NameToIDDictionary.Exists(displayName) Then
			rem outline = Left(inline, ndx) & NameToIDDictionary.Item(displayName)
			rem outline = Left(inline, ndxx) & NameToIDDictionary.Item(displayName)
			outline = inline
			fTo.WriteLine(outline)
		End If
	End If
Loop

fFrom.Close
fTo.Close
rem fLog.Close

Sub LoadXmlTV(dictionary)
	Const TVGFilePath = "TVGMapNames.ini"
	Set fIni = oFSO.OpenTextFile(TVGFilePath, ForReading, False)

	On Error Resume Next

	Do While fIni.AtEndOfStream <> True
		id=""
		displayName = ""
		retline = fIni.ReadLine

	If Not "#" = Left(retline, 1) Then
		ndx = InStr(retline, "=")
		If ndx > 0 Then
			ndxx = InStr(ndx + 1, retline, ",")
			If ndxx > 0 Then
				displayName = Left(retline, ndx-1)
				displayName = LCase(displayName)
				id = Mid(retline, ndxx + 1)
			End If
		End If

		If Len(id) > 0 Then
			dictionary.Add displayName, id
			rem fLog.WriteLine(displayName & "==" & id)
		End If
	End If
		
	Loop

	On Error Goto 0
	fIni.Close
End Sub

Function DecodeUTF8(s)
	Const mult6bit = &h40
	Const DefChar = "?"
    Dim i, c, n, b, d, e

    i = 1
    Do While i <= len(s)
        c = asc(mid(s,i,1))
		n = 0 : d = 0
        If (c and &hE0) = &hC0 Then
			n=1
        Elseif (c and &hF0) = &hE0 Then
			n=2
        Elseif (c and &hF8) = &hF0 Then
			n=3
        Elseif (c and &hFC) = &hF8 Then
			n=4
        Elseif (c and &h80) > 0 Then
            ' Not supported byte
			c = asc(DefChar)
        End If

		If n > 0 and i+n <= len(s) Then
			For it = n to 0 Step -1				
				If it > 0 Then
					e = asc(mid(s,i+it,1))
					If (e and &hC0) <> &h80 Then
						' Unexpected next byte
						d = asc(DefChar)
						Exit For
					Else
						b = e and &h3F
					End If
				Else
					b = c and &h1F
				End If
				d = d + b * mult6bit^(n-it)
			Next
		ElseIf n = 0 Then
			d = c
		Else
			' Unexpected end of string
			d = asc(DefChar)
		End If

		s = left(s,i-1) + chrw(d) + mid(s,i+n+1)
        i = i + 1
    Loop

    DecodeUTF8 = s 
End Function