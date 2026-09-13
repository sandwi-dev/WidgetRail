{ Included inside [Code]. Only called after an explicit uninstall opt-in.
  Do not follow directory junctions/symbolic links into unrelated user files. }
function DataFileAttributes(Path: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function RemoveWidgetProfile(ProfileName: String): Integer;
  external 'DeleteAppContainerProfile@userenv.dll stdcall';

function IsWidgetRailProfile(Name: String): Boolean;
var
  Prefix: String;
  I: Integer;
begin
  Prefix := 'widgetrail.widget.';
  Name := Lowercase(Name);
  Result := False;
  if (Length(Name) <> Length(Prefix) + 32) or
     (Copy(Name, 1, Length(Prefix)) <> Prefix) then Exit;
  for I := Length(Prefix) + 1 to Length(Name) do
    if Pos(Copy(Name, I, 1), '0123456789abcdef') = 0 then Exit;
  Result := True;
end;

function RemoveDataTree(Path: String): Boolean;
var
  Attributes: Cardinal;
  Entry: TFindRec;
begin
  Attributes := DataFileAttributes(Path);
  if Attributes = $FFFFFFFF then begin
    Result := (DLLGetLastError = 2) or (DLLGetLastError = 3);
    Exit;
  end;
  if (Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then begin
    Result := DeleteFile(Path);
    Exit;
  end;
  if (Attributes and $400) <> 0 then begin
    { RemoveDirectory removes the link itself, without traversing its target. }
    Result := RemoveDir(Path);
    Exit;
  end;
  Result := True;
  if FindFirst(AddBackslash(Path) + '*', Entry) then begin
    try
      repeat
        if (Entry.Name <> '.') and (Entry.Name <> '..') then
          if not RemoveDataTree(AddBackslash(Path) + Entry.Name) then Result := False;
      until not FindNext(Entry);
    finally
      FindClose(Entry);
    end;
  end;
  if not RemoveDir(Path) then Result := False;
end;

function RemoveWidgetRailProfiles: Boolean;
var
  MappingKey, Name: String;
  Keys: TArrayOfString;
  I, Code: Integer;
begin
  Result := True;
  MappingKey := 'Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings';
  if not RegKeyExists(HKCU, MappingKey) then Exit;
  if not RegGetSubkeyNames(HKCU, MappingKey, Keys) then begin
    Result := False;
    Exit;
  end;
  for I := 0 to GetArrayLength(Keys) - 1 do begin
    if RegQueryStringValue(HKCU, MappingKey + '\' + Keys[I], 'Moniker', Name) and
       IsWidgetRailProfile(Name) then begin
      Code := RemoveWidgetProfile(Name);
      if Code < 0 then begin
        Log('WidgetRail sandbox profile cleanup failed: ' + IntToStr(Code));
        Result := False;
      end;
    end;
  end;
end;

function RemoveAllWidgetRailData: Boolean;
begin
  Result := RemoveWidgetRailProfiles;
  if not RemoveDataTree(ExpandConstant('{localappdata}\WidgetRail')) then Result := False;
  if not RemoveDataTree(ExpandConstant('{%TEMP}\WidgetRail')) then Result := False;
end;
