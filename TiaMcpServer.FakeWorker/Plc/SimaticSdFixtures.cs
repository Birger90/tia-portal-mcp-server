// SIMATIC SD (.s7dcl/.s7res) fixtures exported from TIA Portal V21 by third parties, used as test data only (never built or run).
// Byte content is reproduced exactly: each text is stored with LF line endings and rebuilt with the recorded line ending and BOM.
//
// Cookbook* : 1748477582/tia-portal-openness-mcp, tools/tiaportal-mcp/skill/lad-cookbook/MCPVerify_*.s7dcl/.s7res,
//             commit 8e7ca425d5d257132ac8cd5ed9cbc318a7c340e2, MIT License, Copyright (c) 2026 1748477582.
// BetterTia*: HonzaT/BetterTIA, editor/tests/samples/Demo_Line/20261012-120000/PLC_1/Blocks/{Safety/FB_Safety,Conveyor/FC_Branches}.s7dcl/.s7res,
//             commit 3d115b30035ad5ea2f10206e5354190ea1ba4de5, MIT License, Copyright (c) 2026 HonzaT.
// See THIRD_PARTY_NOTICES.md. SyntheticExportForms is this repository's own text (no third-party origin).
namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>One .s7dcl/.s7res pair as TIA Portal wrote it: <see cref="S7dcl"/> and <see cref="S7res"/> are the exact texts (BOM as U+FEFF).</summary>
public sealed record SimaticSdFixture(string BlockName, string S7dcl, string S7res)
{
    /// <summary>The get_block_content sd bundle of this block (the .s7dcl first).</summary>
    public string Bundle => $"--- FILE: {BlockName}.s7dcl ---\n{S7dcl}{(S7dcl.EndsWith("\n", StringComparison.Ordinal) ? string.Empty : "\n")}--- FILE: {BlockName}.s7res ---\n{S7res}";
}

/// <summary>The SIMATIC SD fixtures (see the file header for their origin and licence).</summary>
public static class SimaticSdFixtures
{
    // The raw literals hold the checkout's line endings (CRLF on a Windows checkout with core.autocrlf), so they are
    // normalized to LF first; crlf then decides the fixture's endings on every platform.
    private static string Text(string lf, bool crlf, bool bom)
    {
        var normalized = lf.Replace("\r\n", "\n");
        return (bom ? "\uFEFF" : string.Empty) + (crlf ? normalized.Replace("\n", "\r\n") : normalized);
    }

    public static SimaticSdFixture CookbookFbLadV3 { get; } = new("MCPVerify_FB_LAD_v3",
        Text(""""
{
    S7_BlockComment := "MLC_3pC";
    S7_BlockNumber := "59989";
    S7_BlockTitle := "MLC_jr";
    S7_Optimized := "TRUE";
    S7_PreferredLanguage := "LAD";
    S7_Version := "0.1"
}
FUNCTION_BLOCK "MCPVerify_FB_LAD_v3"
    VAR_INPUT
        Trig : Bool;
        PulseBit : Bool;
        NegIn : Bool;
        CmpA : Int;
        CmpB : Int;
    END_VAR
    VAR_OUTPUT
        OutTonQ : Bool;
        OutPulse : Bool;
        OutNot : Bool;
        OutLt : Bool;
    END_VAR
    VAR
        tonInst : TON_TIME;
    END_VAR
    VAR_TEMP
        PulseWork : Bool;
    END_VAR

    {
        S7_Language := "LAD";
        S7_NetworkComment := "MLC_4zs";
        S7_NetworkTitle := "MLC_463"
    }
    NETWORK
        RUNG wire#powerrail
            Contact( #Trig )
            { S7_Templates := "time_type := Time" }
            #tonInst.TON(
                pt := T#500ms,
                et =>  
            )
            Coil( #OutTonQ )
        END_RUNG
    END_NETWORK
    {
        S7_Language := "LAD";
        S7_NetworkComment := "MLC_4iu";
        S7_NetworkTitle := "MLC_3qd"
    }
    NETWORK
        RUNG wire#powerrail
            Contact( #PulseBit )
            P_Trig( #PulseWork )
            Coil( #OutPulse )
        END_RUNG
    END_NETWORK
    {
        S7_Language := "LAD";
        S7_NetworkComment := "MLC_4YN";
        S7_NetworkTitle := "MLC_4Ze"
    }
    NETWORK
        RUNG wire#powerrail
            Contact( #NegIn )
            Not()
            Coil( #OutNot )
        END_RUNG
    END_NETWORK
    {
        S7_Language := "LAD";
        S7_NetworkComment := "MLC_49z";
        S7_NetworkTitle := "MLC_QS"
    }
    NETWORK
        RUNG wire#powerrail
            { S7_Templates := "SrcType := Int" }
            LT_Contact(
                in1 := #CmpA,
                in2 := #CmpB
            )
            Coil( #OutLt )
        END_RUNG
    END_NETWORK
END_FUNCTION_BLOCK
"""" + "\n", crlf: true, bom: true),
        Text(""""
MultiLingualTexts:
  - id: MLC_jr
    zh-CN: MCPVerify_FB_LAD_v3
  - id: MLC_3pC
    zh-CN: TON 实例在 FB.Static（TON_TIME）；PBox/Not/Lt — 安全 PLC 可用
  - id: MLC_463
    zh-CN: N1
  - id: MLC_4zs
    zh-CN: N1 TON Static
  - id: MLC_3qd
    zh-CN: N2
  - id: MLC_4iu
    zh-CN: N2 PBox
  - id: MLC_4Ze
    zh-CN: N3
  - id: MLC_4YN
    zh-CN: N3 Not
  - id: MLC_QS
    zh-CN: N4
  - id: MLC_49z
    zh-CN: N4 Lt
"""" + "\n", crlf: true, bom: true));

    public static SimaticSdFixture CookbookFcLad { get; } = new("MCPVerify_FC_LAD",
        Text(""""
{
    S7_BlockComment := "MLC_548";
    S7_BlockNumber := "901";
    S7_BlockTitle := "MLC_4Vm";
    S7_Optimized := "TRUE";
    S7_PreferredLanguage := "LAD";
    S7_Version := "0.1"
}
FUNCTION "MCPVerify_FC_LAD" : Void
    VAR_INPUT
        "A" : Bool;
        "B" : Bool;
        SET : Bool;
        RESET : Bool;
        V1 : Int;
        V2 : Int;
        VAL : Int;
    END_VAR
    VAR_OUTPUT
        OUT_AND : Bool;
        OUT_OR : Bool;
        OUT_SR : Bool;
        OUT_GT : Bool;
        DST : Int;
        SUM : Int;
    END_VAR

    {
        S7_Language := "LAD";
        S7_NetworkComment := "MLC_4X9";
        S7_NetworkTitle := "MLC_3fA"
    }
    NETWORK
        RUNG wire#powerrail
            Contact( #"A" )
            Contact( #"B" )
            Coil( #OUT_AND )
        END_RUNG
    END_NETWORK
    {
        S7_Language := "LAD";
        S7_NetworkComment := "MLC_4KE";
        S7_NetworkTitle := "MLC_3xC"
    }
    NETWORK
        RUNG wire#powerrail
            Contact( #"A" )
            wire#w1
            Coil( #OUT_OR )
        END_RUNG
        RUNG wire#powerrail
            Contact( #"B" )
        END_RUNG wire#w1
    END_NETWORK
    {
        S7_Language := "LAD";
        S7_NetworkComment := "MLC_4aP";
        S7_NetworkTitle := "MLC_mi"
    }
    NETWORK
        RUNG wire#powerrail
            Contact( #SET )
            S_Coil( #OUT_SR )
        END_RUNG
    END_NETWORK
    {
        S7_Language := "LAD";
        S7_NetworkComment := "MLC_3mb";
        S7_NetworkTitle := "MLC_xY"
    }
    NETWORK
        RUNG wire#powerrail
            Contact( #RESET )
            R_Coil( #OUT_SR )
        END_RUNG
    END_NETWORK
    {
        S7_Language := "LAD";
        S7_NetworkComment := "MLC_36K";
        S7_NetworkTitle := "MLC_4RN"
    }
    NETWORK
        RUNG wire#powerrail
            { S7_Templates := "SrcType := Int" }
            GT_Contact(
                in1 := #VAL,
                in2 := 100
            )
            Coil( #OUT_GT )
        END_RUNG
    END_NETWORK
    {
        S7_Language := "LAD";
        S7_NetworkComment := "MLC_4W9";
        S7_NetworkTitle := "MLC_ki"
    }
    NETWORK
        RUNG wire#powerrail
            
            Move(
                in := 42,
                out1 => #DST
            )
        END_RUNG
    END_NETWORK
    {
        S7_Language := "LAD";
        S7_NetworkComment := "MLC_fp";
        S7_NetworkTitle := "MLC_4yP"
    }
    NETWORK
        RUNG wire#powerrail
            {
                S7_Templates := "SrcType := Int"
            }
            Add(
                in1 := #V1,
                in2 := #V2,
                out => #SUM
            )
        END_RUNG
    END_NETWORK
END_FUNCTION
"""" + "\n", crlf: true, bom: true),
        Text(""""
MultiLingualTexts:
  - id: MLC_4Vm
    zh-CN: MCP LAD 原生指令验证
  - id: MLC_548
    zh-CN: MCP 验证：LAD 原生指令 6 网络（串联/并联/SR/比较/Move/Add）
  - id: MLC_3fA
    zh-CN: N1-串联
  - id: MLC_4X9
    zh-CN: 串联 AND：A & B -> OUT_AND
  - id: MLC_3xC
    zh-CN: N2-并联
  - id: MLC_4KE
    zh-CN: 并联 OR：A | B -> OUT_OR
  - id: MLC_mi
    zh-CN: N3-Set
  - id: MLC_4aP
    zh-CN: 置位：SET -> S OUT_SR
  - id: MLC_xY
    zh-CN: N4-Reset
  - id: MLC_3mb
    zh-CN: 复位：RESET -> R OUT_SR
  - id: MLC_4RN
    zh-CN: N5-比较
  - id: MLC_36K
    zh-CN: 比较：VAL > 100 -> OUT_GT
  - id: MLC_ki
    zh-CN: N6-Move
  - id: MLC_4W9
    zh-CN: 传送：42 -> DST
  - id: MLC_4yP
    zh-CN: N7-加法
  - id: MLC_fp
    zh-CN: 加法：V1 + V2 -> SUM
"""" + "\n", crlf: true, bom: true));

    public static SimaticSdFixture CookbookMixed { get; } = new("MCPVerify_Mixed_LADSCL",
        Text(""""
{
    S7_Optimized := "TRUE";
    S7_PreferredLanguage := "LAD";
    S7_Version := "0.1"
}
FUNCTION_BLOCK "MCPVerify_Mixed_LADSCL"
    VAR_INPUT
        Fwd : Bool;
        Rev : Bool;
        LimFwd : Bool;
        LimRev : Bool;
        Va : Int;
        Vb : Int;
        PulseIn : Bool;
    END_VAR
    VAR_OUTPUT
        RunFwd : Bool;
        RunRev : Bool;
        TmrQ : Bool;
        Sum : Int;
        Pulse : Bool;
    END_VAR
    VAR
        ton1 : TON_TIME;
    END_VAR
    VAR_TEMP
        edgeMem : Bool;
    END_VAR

    // NET1 LAD — direction interlock (该 LAD: 互锁看能流直观)
    { S7_Language := "LAD" }
    NETWORK
        RUNG wire#powerrail
            Contact( #Fwd )
            I_Contact( #RunRev )
            I_Contact( #LimFwd )
            Coil( #RunFwd )
        END_RUNG
        RUNG wire#powerrail
            Contact( #Rev )
            I_Contact( #RunFwd )
            I_Contact( #LimRev )
            Coil( #RunRev )
        END_RUNG
    END_NETWORK

    // NET2 LAD — compare + on-delay timer (Va > Vb held 500ms -> TmrQ)
    { S7_Language := "LAD" }
    NETWORK
        RUNG wire#powerrail
            GT_Contact{ SrcType := Int }( in1 := #Va, in2 := #Vb )
            #ton1.TON{ time_type := Time }( PT := T#500ms, ET => )
            Coil( #TmrQ )
        END_RUNG
    END_NETWORK

    // NET3 SCL — arithmetic (该 SCL: 算术更清楚)
    { S7_Language := "SCL" }
    NETWORK
        #Sum := #Va + #Vb;
    END_NETWORK

    // NET4 LAD — rising edge
    { S7_Language := "LAD" }
    NETWORK
        RUNG wire#powerrail
            Contact( #PulseIn )
            P_Trig( #edgeMem )
            Coil( #Pulse )
        END_RUNG
    END_NETWORK
END_FUNCTION_BLOCK
"""" + "\n", crlf: true, bom: true),
        Text(""""
MultiLingualTexts:
  - id: MLC_placeholder
    zh-CN: x
    en-US: x
"""" + "\n", crlf: true, bom: true));

    public static SimaticSdFixture BetterTiaFbSafety { get; } = new("FB_Safety",
        Text(""""
{
    S7_BlockNumber := "30";
    S7_Optimized := "TRUE";
    S7_PreferredLanguage := "FBD";
    S7_Safety := "TRUE";
    S7_Version := "0.1"
}
FUNCTION_BLOCK "FB_Safety"
    VAR_INPUT
        Start : Bool;
        Service : Bool;
        Fault : Bool;
        Ack_Button : Bool;
        Ack_HMI : Bool;
        Ack_Remote : Bool;
        Door_Closed : Bool;
        Lock_OK : Bool;
        Detector_A : Bool;
        Detector_B : Bool;
    END_VAR
    VAR_OUTPUT
        Enable : Bool;
        EStop_OK : Bool;
        Guard_OK : Bool;
    END_VAR
    VAR
        EStop : ESTOP1;
        Door : SFDOOR;
        Lockout : Bool;
        Ack : Bool;
        Ack_Mem : Bool;
        Ack_Required : Bool;
        Ack_Request : Bool;
        Lamp_Green : Bool;
        Lamp_Red : Bool;
        Fault_Mem : Bool;
        Mem_SR : Bool;
        State_SR : Bool;
        Clear : Bool;
        Always_1 : Bool;
        Step : Int;
        Mismatch_Mem : Bool;
        Mismatch_Edge : Bool;
    END_VAR

    {
        S7_Language := "FBD";
        S7_NetworkTitle := "MLC_1"
    }
    NETWORK
        RUNG #Start
            O( in2 := #Service )
            A(
                in2 := NOT #Fault,
                in3 := "DB_Machine".Safety_OK
            )
            Coil( #Enable )
        END_RUNG
    END_NETWORK
    {
        S7_Language := "FBD";
        S7_NetworkTitle := "MLC_2"
    }
    NETWORK
        RUNG #Ack_Button
            A( in2 := wire#w1 )
            Coil( #Ack )
        END_RUNG
        RUNG #Ack_HMI
            O(
                in2 := #Ack_Remote,
                in3 := NOT #Lockout
            )
        END_RUNG wire#w1
    END_NETWORK
    {
        S7_Language := "FBD";
        S7_NetworkTitle := "MLC_3"
    }
    NETWORK
        RUNG #Door_Closed
            A( in2 := #Lock_OK )
            wire#w2
            Coil( #Guard_OK )
            Coil( #Lamp_Green )
        END_RUNG
        RUNG wire#w2
            NOT Coil( #Lamp_Red )
        END_RUNG
    END_NETWORK
    {
        S7_Language := "FBD";
        S7_NetworkTitle := "MLC_4"
    }
    NETWORK
        RUNG
            {
                f_user_card := "1";
                f_image_card := "0"
            }
            #EStop.ESTOP1(
                E_STOP := "Emergency_Stop",
                ACK_NEC := NOT #Ack_Required,
                ACK := wire#w1,
                TIME_DEL :=  ,
                Q => #EStop_OK,
                Q_DELAY =>  ,
                ACK_REQ => #Ack_Request,
                DIAG =>
            )
        END_RUNG
        RUNG #Ack
            P_Trig( #Ack_Mem )
        END_RUNG wire#w1
    END_NETWORK
    {
        S7_Language := "FBD";
        S7_NetworkTitle := "MLC_5"
    }
    NETWORK
        RUNG
            {
                f_user_card := "1";
                f_image_card := "0"
            }
            #Door.SFDOOR(
                IN1 := "Door_K1",
                IN2 := "Door_K2",
                QBAD_IN1 := NOT "Module_K1".QBAD,
                QBAD_IN2 := NOT "Module_K2".QBAD,
                OPEN_NEC := #Ack_Required,
                ACK_NEC := #Ack_Required,
                ACK := #Ack,
                Q =>  ,
                ACK_REQ =>  ,
                DIAG =>
            )
        END_RUNG
    END_NETWORK
    {
        S7_Language := "FBD";
        S7_NetworkTitle := "MLC_6"
    }
    NETWORK
        RUNG #Fault
            S_Coil( #Fault_Mem )
        END_RUNG
        RUNG #Ack
            R_Coil( #Fault_Mem )
        END_RUNG
        RUNG #Fault_Mem
            S_SR(
                r1 := #Clear,
                operand := #Mem_SR
            )
            Coil( #State_SR )
        END_RUNG
    END_NETWORK
    {
        S7_Language := "FBD";
        S7_NetworkTitle := "MLC_7"
    }
    NETWORK
        RUNG true
            Coil( #Always_1 )
        END_RUNG
        RUNG TRUE
            Move(
                in := 1,
                out1 => #Step
            )
        END_RUNG
    END_NETWORK
    {
        S7_Language := "FBD";
        S7_NetworkTitle := "MLC_8"
    }
    NETWORK
        RUNG #Detector_A
            X( in2 := #Detector_B )
            N_Trig( #Mismatch_Mem )
            Coil( #Mismatch_Edge )
        END_RUNG
    END_NETWORK
    {
        S7_Language := "FBD"
    }
    NETWORK
        RUNG
        END_RUNG
    END_NETWORK
END_FUNCTION_BLOCK
"""" + "\n", crlf: false, bom: false),
        Text(""""
MultiLingualTexts:
  - id: MLC_1
    en-US: Enable — >=1, & with negation, assignment
  - id: MLC_2
    en-US: Acknowledge — input from another rung (wire)
  - id: MLC_3
    en-US: Tap after a box and assignment chain
  - id: MLC_4
    en-US: Emergency stop — F-block, acknowledge edge
  - id: MLC_5
    en-US: Door — F-block with negated pins
  - id: MLC_6
    en-US: Fault memory — S, R, SR
  - id: MLC_7
    en-US: RUNG true and MOVE
  - id: MLC_8
    en-US: Detector mismatch — x and N_TRIG
"""" + "\n", crlf: false, bom: false));

    public static SimaticSdFixture BetterTiaFcBranches { get; } = new("FC_Branches",
        Text(""""
{
    S7_Optimized := "TRUE";
    S7_PreferredLanguage := "LAD";
    S7_BlockNumber := "20";
    S7_Version := "0.1"
}
FUNCTION "FC_Branches" : Void
    VAR_TEMP
        Aux : Int;
    END_VAR

    {
        S7_Language := "LAD";
        S7_NetworkTitle := "MLC_1"
    }
    NETWORK
        RUNG wire#powerrail
            Contact( "Sensor_A" )
            wire#w1
            Contact( "Sensor_B" )
            wire#w2
            Coil( "Output_1" )
        END_RUNG
        RUNG wire#w1
            Contact( "Sensor_C" )
        END_RUNG wire#w2
        RUNG wire#powerrail
            Contact( "Button_1" )
            I_Contact( "Button_2" )
        END_RUNG wire#w1
    END_NETWORK
    {
        S7_Language := "LAD";
        S7_NetworkTitle := "MLC_2"
    }
    NETWORK
        RUNG wire#powerrail
            Contact( "Cycle_Start" )
            wire#w1
            Coil( "Cycle_Running" )
        END_RUNG
        RUNG wire#w1
            Move(
                in := 0,
                out1 => #Aux
            )
        END_RUNG
        RUNG wire#w1
            I_Contact( "Door_Closed" )
            R_Coil( "Enable_Manual" )
        END_RUNG
    END_NETWORK
    {
        S7_Language := "FBD";
        S7_NetworkTitle := "MLC_3"
    }
    NETWORK
        RUNG "Sensor_A"
            O( in2 := "Sensor_B" )
            Coil( "Output_2" )
        END_RUNG
    END_NETWORK
    {
        S7_Language := "LAD";
        S7_NetworkTitle := "MLC_4"
    }
    NETWORK
        RUNG wire#powerrail
            Contact( "M_1" )
            wire#w1
            Contact( "M_2" )
            wire#w3
            Coil( "Bridge" )
        END_RUNG
        RUNG wire#powerrail
            Contact( "M_3" )
            wire#w2
            Contact( "M_4" )
        END_RUNG wire#w3
        RUNG wire#w1
            Contact( "M_5" )
        END_RUNG wire#w2
    END_NETWORK
    {
        S7_Language := "LAD";
        S7_NetworkTitle := "MLC_5"
    }
    NETWORK
        RUNG wire#powerrail
            Contact( "Clock" )
            
            Move(
                in := #Aux,
                out1 => "DB_Machine".Copy
            )
            { S7_Templates := "SrcType := Int" }
            Add(
                in1 := #Aux,
                in2 := 1,
                out => #Aux
            )
        END_RUNG
        RUNG wire#powerrail
            Contact( "Sensor_C" )
            { S7_Templates := "time_type := Time" }
            "DB_T_Rundown".TOF(
                pt := T#500ms,
                et =>  
            )
            Coil( "Rundown" )
        END_RUNG
    END_NETWORK
END_FUNCTION
"""" + "\n", crlf: false, bom: false),
        Text(""""
MultiLingualTexts:
  - id: MLC_1
    en-US: Nested branch and OR at the start
  - id: MLC_2
    en-US: Output branches with a box
  - id: MLC_3
    en-US: FBD network (not drawn by M1)
  - id: MLC_4
    en-US: Bridge — cannot be laid out
  - id: MLC_5
    en-US: Two rungs, chain of boxes
"""" + "\n", crlf: false, bom: false));

    /// <summary>This repository's own text: networks in the export-only forms reported by 1748477582 (Gt{, PBox(, Move{) and one in import form.</summary>
    public static SimaticSdFixture SyntheticExportForms { get; } = new("FC_ExportForms",
        Text("""
{
    S7_Optimized := "TRUE";
    S7_PreferredLanguage := "LAD";
    S7_Version := "0.1"
}
FUNCTION "FC_ExportForms" : Void
    VAR_INPUT
        A : Int;
        B : Int;
        X : Bool;
    END_VAR
    VAR_TEMP
        Mem : Bool;
        Q : Bool;
        D : Int;
    END_VAR

    { S7_Language := "LAD" }
    NETWORK
        RUNG wire#powerrail
            Gt{ SrcType := Int }( #A, #B )
            Coil( #Q )
        END_RUNG
    END_NETWORK
    { S7_Language := "LAD" }
    NETWORK
        RUNG wire#powerrail
            Contact( #X )
            PBox( #Mem )
            Move{ Card:=1; DisableENO:=TRUE }( #A, #D )
        END_RUNG
    END_NETWORK
    { S7_Language := "LAD" }
    NETWORK
        RUNG wire#powerrail
            Contact( #X )
            Coil( #Q )
        END_RUNG
    END_NETWORK
END_FUNCTION
""" + "\n", crlf: false, bom: true),
        Text("""
MultiLingualTexts:
  - id: MLC_x
    en-US: x
""" + "\n", crlf: false, bom: true));

    public static IReadOnlyList<SimaticSdFixture> All { get; } = new[]
    {
        CookbookFbLadV3, CookbookFcLad, CookbookMixed, BetterTiaFbSafety, BetterTiaFcBranches, SyntheticExportForms,
    };
}
