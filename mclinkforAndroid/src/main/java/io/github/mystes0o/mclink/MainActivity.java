package io.github.mystes0o.mclink;

import android.app.Activity;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.net.VpnService;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.InputType;
import android.view.View;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

public final class MainActivity extends Activity {
    private static final int VPN_PERMISSION = 1;
    private static final int DARK = Color.rgb(29, 35, 28);
    private static final int GREEN = Color.rgb(52, 89, 48);
    private final Handler ui = new Handler(Looper.getMainLooper());
    private EditText code;
    private Button join, leave, launch, copy;
    private TextView status;
    private String pendingCode;
    private final Runnable update = new Runnable() {
        @Override public void run() {
            render();
            ui.postDelayed(this, 350);
        }
    };

    @Override protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        if (saved != null) pendingCode = saved.getString("vpnRequest");
        ScrollView scroll = new ScrollView(this);
        scroll.setBackgroundColor(Color.rgb(247, 246, 237));
        scroll.setFillViewport(true);
        scroll.setOnApplyWindowInsetsListener((view, insets) -> {
            view.setPadding(insets.getSystemWindowInsetLeft(), insets.getSystemWindowInsetTop(),
                    insets.getSystemWindowInsetRight(), insets.getSystemWindowInsetBottom());
            return insets;
        });
        getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR);
        LinearLayout content = new LinearLayout(this);
        content.setOrientation(LinearLayout.VERTICAL);
        content.setPadding(dp(24), dp(30), dp(24), dp(24));
        scroll.addView(content);
        setContentView(scroll);

        TextView title = label("MCLink", 36);
        title.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        add(content, title, 0);
        add(content, label("加入 Java 房主的世界", 18), 3);

        code = new EditText(this);
        code.setHint("粘贴房主发来的邀请码");
        code.setContentDescription("房间码");
        code.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_MULTI_LINE
                | InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
        code.setMinLines(2);
        code.setMaxLines(4);
        code.setPadding(dp(14), dp(12), dp(14), dp(12));
        code.setTextSize(15);
        code.setBackground(background(Color.WHITE));
        add(content, code, 24);

        join = button("粘贴房间码并加入", true);
        join.setOnClickListener(view -> pasteAndJoin());
        add(content, join, 12);

        status = label("", 16);
        status.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        status.setPadding(dp(14), dp(14), dp(14), dp(14));
        status.setBackground(background(Color.rgb(233, 238, 223)));
        add(content, status, 24);

        launch = button("打开 Minecraft", true);
        launch.setOnClickListener(view -> openMinecraft());
        add(content, launch, 10);

        copy = button("复制服务器地址", false);
        copy.setOnClickListener(view -> {
            RoomVpn.State room = RoomVpn.state;
            if (room.port == 0) return;
            getSystemService(ClipboardManager.class).setPrimaryClip(
                    ClipData.newPlainText("Minecraft 服务器", room.address()));
            Toast.makeText(this, room.javaRoom ? "已复制。请在 Java 版的「直接连接」中粘贴。" :
                    "已复制。可在 Minecraft 的「服务器」中添加。", Toast.LENGTH_SHORT).show();
        });
        add(content, copy, 8);

        leave = button("离开房间", false);
        leave.setOnClickListener(view -> stopService(new Intent(this, RoomVpn.class)));
        add(content, leave, 8);
        code.addTextChangedListener(new android.text.TextWatcher() {
            @Override public void beforeTextChanged(CharSequence value, int start, int count, int after) {}
            @Override public void onTextChanged(CharSequence value, int start, int before, int count) { render(); }
            @Override public void afterTextChanged(android.text.Editable value) {}
        });
        render();
    }

    private void pasteAndJoin() {
        String input = code.getText().toString();
        if (input.trim().isEmpty()) {
            ClipData clipboard = getSystemService(ClipboardManager.class).getPrimaryClip();
            if (clipboard != null && clipboard.getItemCount() > 0) {
                CharSequence copied = clipboard.getItemAt(0).getText();
                if (copied != null) input = copied.toString();
            }
        }
        RoomTicket room;
        try {
            room = RoomTicket.parse(input);
        } catch (IllegalArgumentException badRoom) {
            RoomVpn.state = new RoomVpn.State(badRoom.getMessage(), 0, false);
            render();
            return;
        }
        if (!room.javaRoom && getPackageManager().getLaunchIntentForPackage(RoomVpn.GAME_PACKAGE) == null) {
            RoomVpn.state = new RoomVpn.State("请先安装 Minecraft 国际基岩版。", 0, false);
            render();
            return;
        }
        pendingCode = input;
        Intent permission = room.javaRoom ? null : VpnService.prepare(this);
        if (permission == null) connect();
        else {
            render();
            startActivityForResult(permission, VPN_PERMISSION);
        }
    }

    private void connect() {
        if (pendingCode == null) return;
        RoomVpn.state = new RoomVpn.State("正在启动房间连接…", 0, true);
        startForegroundService(new Intent(this, RoomVpn.class).putExtra(RoomVpn.EXTRA_TICKET, pendingCode));
        pendingCode = null;
        code.setText("");
        render();
    }

    @Override protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request != VPN_PERMISSION) return;
        if (result == RESULT_OK) connect();
        else {
            pendingCode = null;
            RoomVpn.state = new RoomVpn.State("已取消 VPN 授权。", 0, false);
            render();
        }
    }

    private void openMinecraft() {
        RoomVpn.State room = RoomVpn.state;
        if (room.port == 0 || room.javaRoom) return;
        Intent server = new Intent(Intent.ACTION_VIEW, Uri.parse("minecraft://?addExternalServer="
                + Uri.encode("MCLink|" + RoomTicket.HOST + ":" + room.port))).setPackage(RoomVpn.GAME_PACKAGE);
        try {
            startActivity(server);
        } catch (android.content.ActivityNotFoundException unsupportedLink) {
            Intent game = getPackageManager().getLaunchIntentForPackage(RoomVpn.GAME_PACKAGE);
            if (game != null) startActivity(game);
            else Toast.makeText(this, "请先安装 Minecraft 国际基岩版。", Toast.LENGTH_SHORT).show();
        }
    }

    private void render() {
        RoomVpn.State room = RoomVpn.state;
        String text = room.port == 0 ? room.message : room.message + "\n" + room.address();
        if (!text.contentEquals(status.getText())) status.setText(text);
        code.setEnabled(!room.running && pendingCode == null);
        join.setEnabled(!room.running && pendingCode == null);
        join.setText(code.length() == 0 ? "粘贴房间码并加入" : "加入房间");
        leave.setVisibility(room.running ? View.VISIBLE : View.GONE);
        launch.setVisibility(room.port != 0 && !room.javaRoom ? View.VISIBLE : View.GONE);
        copy.setVisibility(room.port != 0 ? View.VISIBLE : View.GONE);
    }

    @Override protected void onResume() { super.onResume(); ui.post(update); }
    @Override protected void onPause() { ui.removeCallbacks(update); super.onPause(); }
    @Override protected void onSaveInstanceState(Bundle state) {
        super.onSaveInstanceState(state);
        state.putString("vpnRequest", pendingCode);
    }

    private TextView label(String text, int size) {
        TextView view = new TextView(this);
        view.setText(text);
        view.setTextSize(size);
        view.setTextColor(DARK);
        return view;
    }

    private Button button(String text, boolean primary) {
        Button view = new Button(this);
        view.setText(text);
        view.setTextSize(16);
        view.setTextColor(primary ? Color.WHITE : GREEN);
        view.setBackground(background(primary ? GREEN : Color.rgb(228, 235, 219)));
        view.setMinHeight(dp(50));
        view.setOnTouchListener((button, event) -> {
            button.setAlpha(event.getActionMasked() == android.view.MotionEvent.ACTION_DOWN ? .78f : 1f);
            return false;
        });
        return view;
    }

    private GradientDrawable background(int color) {
        GradientDrawable shape = new GradientDrawable();
        shape.setColor(color);
        shape.setCornerRadius(dp(12));
        return shape;
    }

    private void add(LinearLayout parent, View view, int top) {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(-1, -2);
        params.topMargin = dp(top);
        parent.addView(view, params);
    }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
}
