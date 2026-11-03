const App = {
  token: localStorage.getItem('biowork_token') || null,
  currentUser: JSON.parse(localStorage.getItem('biowork_user') || 'null'),
  currentTab: 'dashboard',
  employeesCache: [],
  departmentsCache: [],
  positionRatesCache: [],
  lastPayrollData: null,
  salaryPeriodType: 'Month',
  reportPeriodType: 'Month',
  charts: {},

  init() {
    this.initTheme();
    this.initSidebar();
    this.startClock();
    this.startRealtimeAutoSync();
    this.initPeriodSelectors();

    if (this.token && this.currentUser) {
      this.showMainApp();
    } else {
      this.showLogin();
    }
  },

  initSidebar() {
    if (window.innerWidth > 1024) {
      const saved = localStorage.getItem('biowork_sidebar_collapsed');
      if (saved === '1') {
        document.body.classList.add('sidebar-collapsed');
        const icon = document.getElementById('sidebarToggleIcon');
        if (icon) icon.className = 'ri-menu-unfold-line';
      }
    }
  },

  toggleSidebar() {
    if (typeof window.toggleSidebarMenu === 'function') {
      window.toggleSidebarMenu();
    }
  },

  initTheme() {
    const saved = localStorage.getItem('biowork_theme') || 'light';
    document.documentElement.setAttribute('data-theme', saved);
    const icon = document.getElementById('themeIcon');
    if (icon) icon.className = saved === 'dark' ? 'ri-sun-line' : 'ri-moon-line';
  },

  toggleTheme() {
    const current = document.documentElement.getAttribute('data-theme') || 'light';
    const next = current === 'dark' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', next);
    localStorage.setItem('biowork_theme', next);
    const icon = document.getElementById('themeIcon');
    if (icon) icon.className = next === 'dark' ? 'ri-sun-line' : 'ri-moon-line';
    this.refreshCurrentTab();
    this.applyRoleUI();
  },

  startClock() {
    const update = () => {
      const el = document.getElementById('liveClock');
      if (!el) return;
      const now = new Date();
      el.textContent = now.toLocaleTimeString('vi-VN', { hour12: false }) + ' • ' + now.toLocaleDateString('vi-VN');
    };
    update();
    setInterval(update, 1000);
  },

  startRealtimeAutoSync() {
    // Tự động đồng bộ dữ liệu Realtime ngầm mỗi 3.5 giây giữa Web và App
    setInterval(() => {
      if (!this.token || !this.currentUser) return;
      // Tránh tự động cập nhật nếu đang mở Modal hoặc người dùng đang gõ tìm kiếm
      const hasOpenModal = document.querySelector('.modal-backdrop.show') || document.querySelector('.modal.show');
      if (hasOpenModal) return;
      if (document.activeElement && (document.activeElement.tagName === 'INPUT' || document.activeElement.tagName === 'SELECT')) return;

      if (this.currentTab === 'dashboard') {
        this.loadDashboard({ silent: true });
      } else if (this.currentTab === 'attendance') {
        this.loadAttendance({ silent: true });
      }
    }, 3500);
  },

  getIsoWeek(date) {
    const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
    const dayNum = d.getUTCDay() || 7;
    d.setUTCDate(d.getUTCDate() + 4 - dayNum);
    const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1));
    return Math.ceil((((d - yearStart) / 86400000) + 1) / 7);
  },

  initPeriodSelectors() {
    const now = new Date();
    const curYear = now.getFullYear();
    const curMonth = now.getMonth() + 1;
    const curQuarter = Math.floor((curMonth - 1) / 3) + 1;
    const curWeek = this.getIsoWeek(now);

    ['salYear', 'repYear'].forEach(id => {
      const sel = document.getElementById(id);
      if (!sel) return;
      sel.innerHTML = '';
      for (let y = curYear - 2; y <= curYear + 1; y++) {
        const opt = document.createElement('option');
        opt.value = y;
        opt.textContent = `Năm ${y}`;
        if (y === curYear) opt.selected = true;
        sel.appendChild(opt);
      }
    });

    ['salMonth', 'repMonth'].forEach(id => {
      const sel = document.getElementById(id);
      if (!sel) return;
      sel.innerHTML = '';
      for (let m = 1; m <= 12; m++) {
        const opt = document.createElement('option');
        opt.value = m;
        opt.textContent = `Tháng ${String(m).padStart(2, '0')}`;
        if (m === curMonth) opt.selected = true;
        sel.appendChild(opt);
      }
    });

    ['salWeek', 'repWeek'].forEach(id => {
      const sel = document.getElementById(id);
      if (!sel) return;
      sel.innerHTML = '';
      for (let w = 1; w <= 52; w++) {
        const opt = document.createElement('option');
        opt.value = w;
        opt.textContent = `Tuần ${w}`;
        if (w === curWeek) opt.selected = true;
        sel.appendChild(opt);
      }
    });

    const qSel = document.getElementById('repQuarter');
    if (qSel) qSel.value = String(curQuarter);

    const todayStr = now.toISOString().split('T')[0];
    const firstOfMonthStr = new Date(curYear, now.getMonth(), 1).toISOString().split('T')[0];

    if (document.getElementById('attFromDate')) document.getElementById('attFromDate').value = firstOfMonthStr;
    if (document.getElementById('attToDate')) document.getElementById('attToDate').value = todayStr;
    if (document.getElementById('salFromDate')) document.getElementById('salFromDate').value = firstOfMonthStr;
    if (document.getElementById('salToDate')) document.getElementById('salToDate').value = todayStr;
    if (document.getElementById('manDate')) document.getElementById('manDate').value = todayStr;
  },

  fillLogin(u, p) {
    document.getElementById('loginUsername').value = u;
    document.getElementById('loginPassword').value = p;
  },

  async api(path, options = {}) {
    const headers = { 'Content-Type': 'application/json', ...(options.headers || {}) };
    if (this.token) headers['Authorization'] = `Bearer ${this.token}`;

    const res = await fetch(`/api/${path}`, { ...options, headers });
    if (res.status === 401 && path !== 'Authenticate/login') {
      this.logout();
      throw new Error('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.');
    }

    const data = await res.json().catch(() => ({}));
    return { status: res.status, ok: res.ok, data };
  },

  toast(message, type = 'info') {
    const container = document.getElementById('toastContainer');
    const el = document.createElement('div');
    el.className = `toast ${type}`;
    const icon = type === 'success' ? 'ri-checkbox-circle-fill'
               : type === 'error' ? 'ri-error-warning-fill'
               : 'ri-information-fill';
    el.innerHTML = `<i class="${icon}" style="font-size:18px;"></i> <span>${this.escape(message)}</span>`;
    container.appendChild(el);
    setTimeout(() => el.remove(), 4200);
  },

  showConfirm({
    title = 'Xác Nhận Thao Tác',
    message = 'Bạn có chắc chắn muốn thực hiện thao tác này?',
    confirmText = 'Xác nhận',
    cancelText = 'Hủy bỏ',
    type = 'warning', // 'warning' | 'danger' | 'info' | 'biometric'
    icon = null
  } = {}) {
    return new Promise((resolve) => {
      const modal = document.getElementById('appConfirmModal');
      const titleEl = document.getElementById('confirmTitle');
      const msgEl = document.getElementById('confirmMessage');
      const iconEl = document.getElementById('confirmIcon');
      const iconWrap = document.getElementById('confirmIconWrapper');
      const okBtn = document.getElementById('confirmOkBtn');
      const cancelBtn = document.getElementById('confirmCancelBtn');

      if (!modal || !okBtn || !cancelBtn) {
        resolve(window.confirm(message.replace(/<[^>]+>/g, '')));
        return;
      }

      titleEl.textContent = title;
      msgEl.innerHTML = message;
      cancelBtn.textContent = cancelText;

      iconWrap.style.boxShadow = 'none';
      okBtn.className = 'btn';

      if (type === 'danger') {
        iconEl.className = icon || 'ri-delete-bin-line';
        iconWrap.style.background = 'rgba(239, 68, 68, 0.14)';
        iconWrap.style.color = '#ef4444';
        iconWrap.style.border = '1px solid rgba(239, 68, 68, 0.35)';
        iconWrap.style.boxShadow = '0 0 25px rgba(239, 68, 68, 0.25)';
        okBtn.className = 'btn btn-danger';
        okBtn.innerHTML = `<i class="ri-delete-bin-line"></i> ${confirmText}`;
      } else if (type === 'biometric') {
        iconEl.className = icon || 'ri-fingerprint-line';
        iconWrap.style.background = 'rgba(14, 165, 233, 0.15)';
        iconWrap.style.color = '#38bdf8';
        iconWrap.style.border = '1px solid rgba(14, 165, 233, 0.35)';
        iconWrap.style.boxShadow = '0 0 25px rgba(14, 165, 233, 0.25)';
        okBtn.className = 'btn btn-primary';
        okBtn.innerHTML = `<i class="ri-refresh-line"></i> ${confirmText}`;
      } else if (type === 'info') {
        iconEl.className = icon || 'ri-information-line';
        iconWrap.style.background = 'rgba(59, 130, 246, 0.14)';
        iconWrap.style.color = '#60a5fa';
        iconWrap.style.border = '1px solid rgba(59, 130, 246, 0.35)';
        iconWrap.style.boxShadow = '0 0 25px rgba(59, 130, 246, 0.25)';
        okBtn.className = 'btn btn-primary';
        okBtn.innerHTML = `<i class="ri-check-line"></i> ${confirmText}`;
      } else {
        iconEl.className = icon || 'ri-alert-line';
        iconWrap.style.background = 'rgba(245, 158, 11, 0.14)';
        iconWrap.style.color = '#f59e0b';
        iconWrap.style.border = '1px solid rgba(245, 158, 11, 0.35)';
        iconWrap.style.boxShadow = '0 0 25px rgba(245, 158, 11, 0.25)';
        okBtn.className = 'btn btn-warning';
        okBtn.innerHTML = `<i class="ri-check-line"></i> ${confirmText}`;
      }

      let isResolved = false;
      const cleanup = () => {
        if (isResolved) return;
        isResolved = true;
        modal.classList.add('hidden');
        okBtn.onclick = null;
        cancelBtn.onclick = null;
        modal.onclick = null;
        document.removeEventListener('keydown', onKey);
      };

      const onKey = (e) => {
        if (e.key === 'Escape') {
          cleanup();
          resolve(false);
        } else if (e.key === 'Enter') {
          cleanup();
          resolve(true);
        }
      };

      okBtn.onclick = () => {
        cleanup();
        resolve(true);
      };

      cancelBtn.onclick = () => {
        cleanup();
        resolve(false);
      };

      modal.onclick = (e) => {
        if (e.target === modal) {
          cleanup();
          resolve(false);
        }
      };

      document.addEventListener('keydown', onKey);
      modal.classList.remove('hidden');
      setTimeout(() => okBtn.focus(), 60);
    });
  },

  escape(str) {
    if (str === null || str === undefined) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  },

  formatVnd(num) {
    return Number(num || 0).toLocaleString('vi-VN') + ' đ';
  },

  formatDate(d) {
    if (!d) return '--';
    const dt = new Date(d);
    return dt.toLocaleDateString('vi-VN');
  },

  formatTime(d) {
    if (!d) return '--:--';
    const dt = new Date(d);
    return dt.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });
  },

  async handleLogin(e) {
    e.preventDefault();
    const errEl = document.getElementById('loginError');
    const btn = document.getElementById('loginBtn');
    errEl.classList.add('hidden');
    btn.disabled = true;

    try {
      const username = document.getElementById('loginUsername').value.trim();
      const password = document.getElementById('loginPassword').value;

      const { ok, data } = await this.api('Authenticate/login', {
        method: 'POST',
        body: JSON.stringify({ username, password, client: 'web' })
      });

      if (!ok || !data.success) {
        errEl.textContent = data.message || 'Tên đăng nhập hoặc mật khẩu không đúng.';
        errEl.classList.remove('hidden');
        return;
      }

      this.token = data.token || data.data?.token;
      this.currentUser = data.data;
      localStorage.setItem('biowork_token', this.token);
      localStorage.setItem('biowork_user', JSON.stringify(this.currentUser));
      this.toast(`Xin chào ${this.currentUser.fullName}!`, 'success');
      this.showMainApp();
    } catch (err) {
      errEl.textContent = err.message || 'Không thể kết nối máy chủ.';
      errEl.classList.remove('hidden');
    } finally {
      btn.disabled = false;
    }
  },

  logout() {
    this.token = null;
    this.currentUser = null;
    localStorage.removeItem('biowork_token');
    localStorage.removeItem('biowork_user');
    this.showLogin();
  },

  showLogin() {
    document.getElementById('loginView').classList.remove('hidden');
    document.getElementById('appView').classList.add('hidden');
  },

  applyRoleUI() {
    const isAdmin = this.currentUser && this.currentUser.role === 'Admin';
    if (isAdmin) {
      document.body.classList.remove('role-manager');
      document.body.classList.add('role-admin');
    } else {
      document.body.classList.remove('role-admin');
      document.body.classList.add('role-manager');
    }
    document.querySelectorAll('.admin-only').forEach(el => {
      el.style.display = isAdmin ? '' : 'none';
    });
  },

  async showMainApp() {
    document.getElementById('loginView').classList.add('hidden');
    document.getElementById('appView').classList.remove('hidden');

    if (this.currentUser) {
      document.getElementById('userFullName').textContent = this.currentUser.fullName || this.currentUser.username;
      document.getElementById('userRoleBadge').textContent =
        this.currentUser.role === 'Admin' ? 'Quản trị viên (Admin)' : 'Quản lý (Manager)';
      document.getElementById('userAvatar').textContent =
        (this.currentUser.fullName || 'A').trim().charAt(0).toUpperCase();

      this.applyRoleUI();
    }

    await this.loadCommonMetadata();
    this.switchTab(this.currentTab || 'dashboard');
  },

  async loadCommonMetadata() {
    try {
      const [empRes, deptRes] = await Promise.all([
        this.api('Employee'),
        this.api('Employee/departments')
      ]);

      if (empRes.ok && empRes.data?.data) {
        this.employeesCache = empRes.data.data;
        this.populateEmployeeSelects();
      }
      if (deptRes.ok && deptRes.data?.data) {
        this.departmentsCache = deptRes.data.data;
        this.populateDepartmentSelects();
      }
      await this.loadPositionRates();
    } catch (_) {}
  },

  populateDepartmentSelects() {
    ['empDeptFilter', 'attDeptFilter', 'salDeptFilter', 'repDeptFilter'].forEach(id => {
      const sel = document.getElementById(id);
      if (!sel) return;
      const cur = sel.value;
      sel.innerHTML = '<option value="">Tất cả phòng ban</option>';
      this.departmentsCache.forEach(d => {
        const opt = document.createElement('option');
        opt.value = d;
        opt.textContent = d;
        sel.appendChild(opt);
      });
      sel.value = cur;
    });
  },

  populateEmployeeSelects() {
    const attSel = document.getElementById('attUserFilter');
    const manSel = document.getElementById('manUserId');
    const isManager = this.currentUser?.role === 'Manager';
    const myId = (this.currentUser?.userId || this.currentUser?.id || '').toLowerCase();
    const myUsername = (this.currentUser?.username || '').toLowerCase();

    // Xem lịch sử: cho phép xem toàn bộ nhân sự (trừ Admin không chấm công)
    const staff = this.employeesCache.filter(e => e.role !== 'Admin');

    // Chấm công bù:
    // - Admin: Có quyền chấm bù cho cả Nhân viên và Quản lý
    // - Quản lý: Chỉ được phép chấm bù cho Nhân viên cấp dưới (Tuyệt đối không chấm cho Admin và không tự chấm bù cho chính mình)
    const assignableStaff = isManager
      ? this.employeesCache.filter(e => {
          const eId = (e.id || e.userId || '').toLowerCase();
          const eUser = (e.username || '').toLowerCase();
          return e.role === 'User' && eId !== myId && eUser !== myUsername;
        })
      : staff;

    if (attSel) {
      const cur = attSel.value;
      attSel.innerHTML = '<option value="">Tất cả nhân viên</option>';
      staff.forEach(e => {
        const opt = document.createElement('option');
        opt.value = e.id;
        opt.textContent = `${e.fullName} (${e.department || 'N/A'})`;
        attSel.appendChild(opt);
      });
      attSel.value = cur;
    }

    if (manSel) {
      const cur = manSel.value;
      manSel.innerHTML = '';
      assignableStaff.forEach(e => {
        const opt = document.createElement('option');
        opt.value = e.id;
        opt.textContent = `${e.fullName} — ${e.department || 'Chưa phân ban'} (@${e.username})`;
        manSel.appendChild(opt);
      });
      if (cur) manSel.value = cur;
    }
  },

  switchTab(tab) {
    this.currentTab = tab;
    document.querySelectorAll('.nav-item[data-tab]').forEach(btn => {
      btn.classList.toggle('active', btn.dataset.tab === tab);
    });
    document.querySelectorAll('.tab-pane').forEach(sec => {
      sec.classList.toggle('hidden', sec.id !== `tab-${tab}`);
    });

    const titles = {
      dashboard: ['Tổng Quan Hệ Thống & Biểu Đồ', 'Giám sát thời gian thực tình hình nhân sự, điểm danh vân tay và quỹ lương'],
      employees: ['Quản Lý Tài Khoản Nhân Viên & Quản Lý', 'Thêm mới, cập nhật, đóng/mở khóa tài khoản và quản lý vân tay thiết bị chính chủ'],
      attendance: ['Giám Sát Điểm Danh & Chấm Công Thủ Công', 'Theo dõi giờ vào/ra của nhân viên và chấm công thủ công khi quên chấm công'],
      salary: ['Tính Lương Hàng Tuần / Hàng Tháng', 'Tự động tính lương theo số giờ làm việc (Giờ chuẩn tối đa 8h và Tăng ca OT x1.5)'],
      reports: ['Báo Cáo Thống Kê Tuần / Tháng / Quý / Năm & Email', 'Phân tích dữ liệu chuyên cần và gửi báo cáo thống kê qua Email đã đăng ký']
    };

    const [t, s] = titles[tab] || titles.dashboard;
    document.getElementById('pageTitle').textContent = t;
    document.getElementById('pageSubtitle').textContent = s;

    this.refreshCurrentTab();
    this.applyRoleUI();
  },

  refreshCurrentTab() {
    if (!this.token) return;
    if (this.currentTab === 'dashboard') this.loadDashboard();
    else if (this.currentTab === 'employees') this.loadEmployees();
    else if (this.currentTab === 'attendance') this.loadAttendance();
    else if (this.currentTab === 'salary') this.loadSalaries();
    else if (this.currentTab === 'reports') this.loadReports();
  },

  // ==================== TAB 1: DASHBOARD ====================
  async loadDashboard(options = {}) {
    try {
      const { ok, data } = await this.api('Report/dashboard');
      if (!ok || !data.data) return;
      const d = data.data;

      document.getElementById('dashMonthLabel').textContent = d.monthLabel;

      const kpiHtml = `
        <div class="kpi-card">
          <div>
            <div class="kpi-label">Tổng Nhân Sự Công Ty</div>
            <div class="kpi-value">${d.totalEmployees}</div>
            <div class="kpi-sub"><strong style="color:var(--success)">${d.activeEmployees}</strong> hoạt động • <strong>${d.lockedEmployees}</strong> đã khóa</div>
          </div>
          <div class="kpi-icon blue"><i class="ri-team-line"></i></div>
        </div>
        <div class="kpi-card">
          <div>
            <div class="kpi-label">Điểm Danh Hôm Nay</div>
            <div class="kpi-value">${d.checkedInToday} / ${d.activeEmployees}</div>
            <div class="kpi-sub"><strong style="color:var(--primary)">${d.currentlyWorking}</strong> đang làm • <strong>${d.completedToday}</strong> đã tan ca</div>
          </div>
          <div class="kpi-icon green"><i class="ri-fingerprint-line"></i></div>
        </div>
        <div class="kpi-card">
          <div>
            <div class="kpi-label">Thiết Bị Vân Tay Chính Chủ</div>
            <div class="kpi-value">${d.biometricEnrolledCount} / ${d.activeEmployees}</div>
            <div class="kpi-sub">Đã đăng ký khóa phần cứng chống chấm hộ</div>
          </div>
          <div class="kpi-icon purple"><i class="ri-shield-keyhole-line"></i></div>
        </div>
        <div class="kpi-card">
          <div>
            <div class="kpi-label">Tổng Giờ Làm (${d.monthLabel})</div>
            <div class="kpi-value">${d.hoursThisMonth}h</div>
            <div class="kpi-sub">Tăng ca OT: <strong style="color:var(--warning)">${d.overtimeHoursThisMonth}h</strong> • Chấm bù: <strong>${d.manualEntriesThisMonth}</strong></div>
          </div>
          <div class="kpi-icon amber"><i class="ri-time-line"></i></div>
        </div>
        <div class="kpi-card">
          <div>
            <div class="kpi-label">Quỹ Lương Tạm Tính (${d.monthLabel})</div>
            <div class="kpi-value" style="color:var(--success);">${this.formatVnd(d.payrollThisMonth)}</div>
            <div class="kpi-sub">Tự động tính từ tổng giờ chuẩn &amp; OT</div>
          </div>
          <div class="kpi-icon green"><i class="ri-money-dollar-circle-line"></i></div>
        </div>
      `;
      document.getElementById('dashboardKpis').innerHTML = kpiHtml;

      this.renderLast14DaysChart(d.last14Days || []);
      this.renderDepartmentsChart('chartDashDepartments', d.departments || []);

      const tbody = document.getElementById('todayStatusBody');
      tbody.innerHTML = (d.todayAttendance || []).map(item => {
        const badge = item.status === 'Completed'
          ? '<span class="badge badge-success"><i class="ri-check-double-line"></i> Đã hoàn thành</span>'
          : item.status === 'Working'
            ? '<span class="badge badge-primary"><i class="ri-Loader-4-line"></i> Đang trong ca</span>'
            : '<span class="badge badge-muted">Chưa vào ca</span>';

        return `
          <tr>
            <td><strong>${this.escape(item.fullName)}</strong></td>
            <td>${this.escape(item.department || 'Chưa phân ban')} <span style="color:var(--text-muted); font-size:12px;">• ${this.escape(item.position || 'Nhân viên')}</span></td>
            <td><strong>${item.checkInTime ? this.formatTime(item.checkInTime) : '--:--'}</strong></td>
            <td><strong>${item.checkOutTime ? this.formatTime(item.checkOutTime) : '--:--'}</strong></td>
            <td><strong>${item.hours}h</strong> ${item.isManual ? '<span class="badge badge-warning" style="margin-left:4px;">Thủ công</span>' : ''}</td>
            <td>${badge}</td>
            <td style="text-align:right;">
              <button class="btn btn-outline btn-sm" onclick="App.openManualModal('${item.userId}')">
                <i class="ri-edit-line"></i> Chấm công thủ công
              </button>
            </td>
          </tr>
        `;
      }).join('');
    } catch (e) {
      if (!options.silent) this.toast(e.message, 'error');
    }
  },

  renderLast14DaysChart(points) {
    const ctx = document.getElementById('chartLast14Days');
    if (!ctx) return;
    if (this.charts.last14) this.charts.last14.destroy();

    const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
    const textColor = isDark ? '#94a3b8' : '#64748b';
    const gridColor = isDark ? 'rgba(255,255,255,0.06)' : 'rgba(15,23,42,0.06)';

    this.charts.last14 = new Chart(ctx, {
      data: {
        labels: points.map(p => p.label),
        datasets: [
          {
            type: 'bar',
            label: 'Tổng số giờ làm (h)',
            data: points.map(p => p.hours),
            backgroundColor: 'rgba(37, 99, 235, 0.78)',
            borderRadius: 6,
            yAxisID: 'y'
          },
          {
            type: 'line',
            label: 'Số nhân viên đi làm',
            data: points.map(p => p.checkIns),
            borderColor: '#10b981',
            backgroundColor: '#10b981',
            borderWidth: 2.5,
            tension: 0.35,
            yAxisID: 'y1'
          }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { labels: { color: textColor, font: { weight: '600' } } }
        },
        scales: {
          x: { ticks: { color: textColor }, grid: { display: false } },
          y: { position: 'left', ticks: { color: textColor }, grid: { color: gridColor }, title: { display: true, text: 'Số giờ (h)', color: textColor } },
          y1: { position: 'right', ticks: { color: textColor, stepSize: 1 }, grid: { drawOnChartArea: false }, title: { display: true, text: 'Nhân viên', color: textColor } }
        }
      }
    });
  },

  renderDepartmentsChart(canvasId, depts) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;
    if (this.charts[canvasId]) this.charts[canvasId].destroy();

    const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
    const textColor = isDark ? '#cbd5e1' : '#475569';
    const colors = ['#2563eb', '#10b981', '#f59e0b', '#8b5cf6', '#0ea5e9', '#ec4899'];

    this.charts[canvasId] = new Chart(ctx, {
      type: 'doughnut',
      data: {
        labels: depts.map(d => `${d.department} (${d.totalHoursWorked}h)`),
        datasets: [{
          data: depts.map(d => d.totalHoursWorked),
          backgroundColor: colors,
          borderWidth: 2,
          borderColor: isDark ? '#111827' : '#ffffff'
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        cutout: '64%',
        plugins: {
          legend: { position: 'bottom', labels: { color: textColor, padding: 14, font: { size: 12, weight: '600' } } }
        }
      }
    });
  },

  // ==================== TAB 2: EMPLOYEES ====================
  async loadEmployees() {
    const search = document.getElementById('empSearch')?.value.trim() || '';
    const dept = document.getElementById('empDeptFilter')?.value || '';
    const role = document.getElementById('empRoleFilter')?.value || '';
    const status = document.getElementById('empStatusFilter')?.value || '';

    const params = new URLSearchParams();
    if (search) params.set('searchTerm', search);
    if (dept) params.set('department', dept);
    if (role) params.set('role', role);
    if (status) params.set('isActive', status);

    const { ok, data } = await this.api(`Employee?${params.toString()}`);
    if (!ok || !data.data) return;

    this.employeesCache = data.data;
    const isAdmin = this.currentUser?.role === 'Admin';
    const tbody = document.getElementById('employeesTableBody');

    tbody.innerHTML = data.data.map(u => {
      const roleBadge = u.role === 'Admin'
        ? '<span class="badge badge-danger"><i class="ri-shield-star-line"></i> Admin</span>'
        : u.role === 'Manager'
          ? '<span class="badge badge-purple"><i class="ri-user-star-line"></i> Quản lý</span>'
          : '<span class="badge badge-primary"><i class="ri-user-line"></i> Nhân viên</span>';

      const bioBadge = u.isBiometricEnrolled
        ? `<span class="badge badge-success" title="Thiết bị: ${this.escape(u.biometricDeviceId || '')}"><i class="ri-fingerprint-line"></i> Đã đăng ký vân tay</span>`
        : `<span class="badge badge-warning"><i class="ri-error-warning-line"></i> Chưa đăng ký</span>`;

      const statusBadge = u.isActive
        ? '<span class="badge badge-success">Đang hoạt động</span>'
        : '<span class="badge badge-danger">Đã đóng (Khóa)</span>';

      const initial = (u.fullName || 'N').trim().charAt(0).toUpperCase();
      const dobStr = u.dateOfBirth ? this.formatDate(u.dateOfBirth) : 'Chưa cập nhật';
      const hireStr = u.hireDate ? this.formatDate(u.hireDate) : '--';
      const genderStr = u.gender || '--';
      const cccdStr = u.citizenId || '--';

      return `
        <tr>
          <td>
            <div class="emp-cell">
              <div class="avatar-circle">${this.escape(initial)}</div>
              <div class="emp-meta">
                <strong>${this.escape(u.fullName)} <span style="font-weight:500; font-size:11px; color:var(--text-muted);">(${this.escape(genderStr)} • NS: ${dobStr})</span></strong>
                <span>@${this.escape(u.username)} • ${this.escape(u.email)}</span>
                <span style="font-size:11px; color:var(--text-muted);">SĐT: ${this.escape(u.phoneNumber || '--')} • CCCD: ${this.escape(cccdStr)}</span>
                ${u.address ? `<span style="font-size:11px; color:var(--text-muted);"><i class="ri-map-pin-line"></i> ${this.escape(u.address)}</span>` : ''}
              </div>
            </div>
          </td>
          <td>
            <strong>${this.escape(u.department || 'Chưa phân ban')}</strong>
            <div style="font-size:12px; color:var(--primary); font-weight:600;">${this.escape(u.position || 'Nhân viên chính thức')}</div>
            <div style="font-size:11px; color:var(--text-muted); margin-top:2px;">
              <i class="ri-time-line"></i> ${this.escape(u.assignedShift || 'Ca Hành Chính (08:00 - 17:00)')} • Vào: ${hireStr}
            </div>
          </td>
          <td>${roleBadge}</td>
          <td>
            <strong>${this.formatVnd(u.currentHourlyRate || 55000)}/h</strong>
            <div style="font-size:11.5px; color:var(--text-muted);">OT x${u.overtimeRateMultiplier || 1.5} • Chuẩn ${u.standardDailyHours || 8}h/ngày</div>
          </td>
          <td>
            ${bioBadge}
            ${u.isBiometricEnrolled ? `
              <div style="margin-top:4px;">
                <button class="btn btn-outline btn-sm" style="padding:3px 8px; font-size:11px;" onclick="App.resetBiometric('${u.id}', '${this.escape(u.fullName)}')">
                  <i class="ri-refresh-line"></i> Reset thiết bị
                </button>
              </div>` : ''}
          </td>
          <td>${statusBadge}</td>
          <td style="text-align:right;">
            <div style="display:inline-flex; gap:6px; flex-wrap:wrap; justify-content:flex-end;">
              ${isAdmin ? `
                <button class="btn btn-outline btn-sm" onclick="App.openEmployeeModal('${u.id}')" title="Cập nhật thông tin & lương">
                  <i class="ri-edit-line"></i> Sửa
                </button>
                <button class="btn ${u.isActive ? 'btn-warning' : 'btn-success'} btn-sm" onclick="App.toggleUserStatus('${u.id}', ${!u.isActive}, '${this.escape(u.fullName)}')" title="${u.isActive ? 'Đóng / Khóa tài khoản' : 'Mở khóa tài khoản'}">
                  <i class="${u.isActive ? 'ri-lock-line' : 'ri-lock-unlock-line'}"></i> ${u.isActive ? 'Đóng' : 'Mở'}
                </button>
                <button class="btn btn-danger btn-sm" onclick="App.deleteEmployee('${u.id}', '${this.escape(u.fullName)}')" title="Xóa tài khoản">
                  <i class="ri-delete-bin-line"></i>
                </button>
              ` : `
                ${u.role === 'Admin' ? `
                  <span class="badge badge-secondary" style="font-size:11px; padding:5px 8px;">Tài khoản Quản trị</span>
                ` : u.id === this.currentUser?.id ? `
                  <span class="badge badge-purple" style="font-size:11px; padding:5px 8px;" title="Để đảm bảo kiểm soát chéo, chỉ Admin mới được duyệt chấm bù cho Quản lý">Chỉ Admin duyệt</span>
                ` : `
                  <button class="btn btn-outline btn-sm" onclick="App.openManualModal('${u.id}')">
                    <i class="ri-calendar-check-line"></i> Chấm bù
                  </button>
                `}
              `}
            </div>
          </td>
        </tr>
      `;
    }).join('');
  },

  onPositionPresetChange() {
    const val = document.getElementById('empPositionPreset')?.value || '';
    if (!val || val === 'custom') return;
    const parts = val.split('|');
    if (parts.length >= 3) {
      document.getElementById('empPosition').value = parts[0];
      document.getElementById('empHourlyRate').value = Number(parts[1]);
      document.getElementById('empRole').value = parts[2];
    }
  },

  syncPositionPresetDropdown(positionText, hourlyRate, role) {
    const sel = document.getElementById('empPositionPreset');
    if (!sel) return;
    const posLower = (positionText || '').trim().toLowerCase();
    const rateNum = Number(hourlyRate) || 0;
    const roleVal = (role || '').trim().toLowerCase();

    const validOptions = Array.from(sel.options).filter(opt => opt.value && opt.value !== 'custom');
    if (!validOptions.length) {
      sel.value = 'custom';
      return;
    }

    // 1. Khớp chính xác tên chức danh
    let matchedOpt = validOptions.find(opt => {
      const [pName] = opt.value.split('|');
      const pLower = pName.toLowerCase();
      return pLower === posLower || 
             posLower === pLower.split(' / ')[0].toLowerCase() ||
             pLower === posLower.split(' / ')[0].toLowerCase();
    });

    // 2. Khớp chính xác theo mức lương theo giờ (Hourly Rate)
    if (!matchedOpt && rateNum > 0) {
      const rateMatches = validOptions.filter(opt => {
        const parts = opt.value.split('|');
        return Number(parts[1]) === rateNum;
      });
      if (rateMatches.length === 1) {
        matchedOpt = rateMatches[0];
      } else if (rateMatches.length > 1) {
        matchedOpt = rateMatches.find(opt => opt.value.split('|')[2]?.toLowerCase() === roleVal) || rateMatches[0];
      }
    }

    // 3. Khớp theo vai trò & từ khóa chức danh thực tế
    if (!matchedOpt) {
      if (roleVal === 'admin' || posLower.includes('giám đốc') || posLower.includes('admin') || posLower.includes('ceo') || posLower.includes('quản trị')) {
        matchedOpt = validOptions.find(opt => opt.value.includes('|Admin') || opt.text.toLowerCase().includes('giám đốc'));
      } else if (roleVal === 'manager' || posLower.includes('trưởng phòng') || posLower.includes('quản lý') || posLower.includes('kế toán trưởng') || posLower.includes('trưởng ban')) {
        matchedOpt = validOptions.find(opt => opt.value.includes('|Manager') || opt.text.toLowerCase().includes('trưởng phòng') || opt.text.toLowerCase().includes('quản lý'));
      } else if (posLower.includes('trưởng nhóm') || posLower.includes('team lead') || posLower.includes('leader') || posLower.includes('nhóm trưởng')) {
        matchedOpt = validOptions.find(opt => opt.text.toLowerCase().includes('trưởng nhóm') || opt.text.toLowerCase().includes('lead'));
      } else if (posLower.includes('senior') || posLower.includes('cao cấp') || posLower.includes('chuyên viên') || posLower.includes('lập trình')) {
        matchedOpt = validOptions.find(opt => opt.text.toLowerCase().includes('cao cấp') || opt.text.toLowerCase().includes('senior'));
      } else if (posLower.includes('thử việc') || posLower.includes('probation')) {
        matchedOpt = validOptions.find(opt => opt.text.toLowerCase().includes('thử việc'));
      } else if (posLower.includes('thực tập') || posLower.includes('intern')) {
        matchedOpt = validOptions.find(opt => opt.text.toLowerCase().includes('thực tập'));
      }
    }

    // 4. Khớp theo mức lương gần nhất
    if (!matchedOpt && rateNum > 0) {
      let minDiff = Infinity;
      let closest = null;
      for (const opt of validOptions) {
        const optRate = Number(opt.value.split('|')[1]) || 0;
        const diff = Math.abs(optRate - rateNum);
        if (diff < minDiff) {
          minDiff = diff;
          closest = opt;
        }
      }
      if (closest && minDiff <= 35000) {
        matchedOpt = closest;
      }
    }

    if (matchedOpt) {
      sel.value = matchedOpt.value;
    } else {
      sel.value = 'custom';
    }
  },

  openEmployeeModal(userId = null) {
    const modal = document.getElementById('employeeModal');
    const form = document.getElementById('employeeForm');
    form.reset();
    document.getElementById('empId').value = userId || '';

    const userInput = document.getElementById('empUsername');
    const pwdInput = document.getElementById('empPassword');
    const pwdLabel = document.getElementById('empPasswordLabel');

    userInput.disabled = false;
    userInput.readOnly = false;
    userInput.removeAttribute('disabled');
    userInput.removeAttribute('readonly');
    userInput.style.opacity = '1';
    userInput.style.cursor = 'text';

    if (userId) {
      const u = this.employeesCache.find(x => x.id === userId);
      if (!u) return;
      document.getElementById('empModalTitle').innerHTML = `<i class="ri-user-settings-line"></i> Cập Nhật Hồ Sơ Nhân Sự: ${this.escape(u.fullName)}`;
      userInput.value = u.username || '';
      userInput.disabled = false;
      userInput.readOnly = false;
      userInput.removeAttribute('disabled');
      userInput.removeAttribute('readonly');
      userInput.style.opacity = '1';
      userInput.style.cursor = 'text';
      document.getElementById('empFullName').value = u.fullName || '';
      document.getElementById('empEmail').value = u.email || '';
      document.getElementById('empPhone').value = u.phoneNumber || '';
      document.getElementById('empDepartment').value = u.department || '';
      document.getElementById('empPosition').value = u.position || 'Nhân Viên Chính Thức';
            if (document.getElementById('empAssignedShift')) {
        let sVal = u.assignedShift || 'Ca Hành Chính (08:00 - 17:00)';
        if (sVal.includes('06:00') || sVal.includes('Sáng')) {
          sVal = 'Ca Sáng (06:00 - 12:00)';
        } else if (sVal.includes('14:00') || sVal.includes('Chiều')) {
          sVal = 'Ca Chiều (14:00 - 22:00)';
        } else if (sVal.includes('18:00') || sVal.includes('Tối')) {
          sVal = 'Ca Tối (18:00 - 22:00)';
        } else if (sVal.includes('linh hoạt') || sVal.includes('Xoay')) {
          sVal = 'Xoay ca linh hoạt';
        }
        document.getElementById('empAssignedShift').value = sVal;
      }
      document.getElementById('empRole').value = u.role || 'User';
      document.getElementById('empHourlyRate').value = u.currentHourlyRate || 55000;
      document.getElementById('empOtMultiplier').value = u.overtimeRateMultiplier || 1.5;
      document.getElementById('empStdHours').value = u.standardDailyHours || 8;
      document.getElementById('empDob').value = u.dateOfBirth ? String(u.dateOfBirth).split('T')[0] : '';
      document.getElementById('empGender').value = u.gender || 'Nam';
      document.getElementById('empCitizenId').value = u.citizenId || '';
      document.getElementById('empAddress').value = u.address || '';
      document.getElementById('empHireDate').value = u.hireDate ? String(u.hireDate).split('T')[0] : '';
      this.syncPositionPresetDropdown(u.position, u.currentHourlyRate, u.role);

      pwdInput.required = false;
      pwdInput.placeholder = 'Bỏ trống nếu giữ nguyên mật khẩu cũ';
      pwdLabel.textContent = 'Đặt lại mật khẩu mới (Tùy chọn)';
    } else {
      document.getElementById('empModalTitle').innerHTML = '<i class="ri-user-add-line"></i> Thêm Hồ Sơ Nhân Viên / Quản Lý Mới';
      userInput.disabled = false;
      document.getElementById('empHireDate').value = new Date().toISOString().split('T')[0];
      if (document.getElementById('empAssignedShift')) {
        document.getElementById('empAssignedShift').value = 'Ca Hành Chính (08:00 - 17:00)';
      }
      document.getElementById('empPositionPreset').value = 'Nhân Viên Chính Thức|55000|User';
      this.onPositionPresetChange();
      pwdInput.required = true;
      pwdInput.placeholder = 'Tối thiểu 6 ký tự (có chữ số)';
      pwdLabel.textContent = 'Mật khẩu khởi tạo *';
    }

    modal.classList.remove('hidden');
  },

  async submitEmployeeForm(e) {
    e.preventDefault();
    const id = document.getElementById('empId').value;
    const isEdit = Boolean(id);

    const dobVal = document.getElementById('empDob').value || null;
    const hireVal = document.getElementById('empHireDate').value || null;
    const genderVal = document.getElementById('empGender').value || null;
    const citizenVal = document.getElementById('empCitizenId').value.trim() || null;
    const addressVal = document.getElementById('empAddress').value.trim() || null;
    const assignedShiftVal = document.getElementById('empAssignedShift')?.value || 'Ca Hành Chính (08:00 - 17:00)';

    const payload = isEdit
      ? {
          username: document.getElementById('empUsername').value.trim(),
          fullName: document.getElementById('empFullName').value.trim(),
          email: document.getElementById('empEmail').value.trim(),
          phoneNumber: document.getElementById('empPhone').value.trim(),
          department: document.getElementById('empDepartment').value.trim(),
          position: document.getElementById('empPosition').value.trim(),
          assignedShift: assignedShiftVal,
          dateOfBirth: dobVal,
          gender: genderVal,
          citizenId: citizenVal,
          address: addressVal,
          hireDate: hireVal,
          role: document.getElementById('empRole').value,
          newPassword: document.getElementById('empPassword').value || null,
          hourlyRate: Number(document.getElementById('empHourlyRate').value),
          overtimeRateMultiplier: Number(document.getElementById('empOtMultiplier').value),
          standardDailyHours: Number(document.getElementById('empStdHours').value)
        }
      : {
          username: document.getElementById('empUsername').value.trim(),
          fullName: document.getElementById('empFullName').value.trim(),
          email: document.getElementById('empEmail').value.trim(),
          phoneNumber: document.getElementById('empPhone').value.trim(),
          department: document.getElementById('empDepartment').value.trim(),
          position: document.getElementById('empPosition').value.trim(),
          assignedShift: assignedShiftVal,
          dateOfBirth: dobVal,
          gender: genderVal,
          citizenId: citizenVal,
          address: addressVal,
          hireDate: hireVal,
          role: document.getElementById('empRole').value,
          password: document.getElementById('empPassword').value,
          initialHourlyRate: Number(document.getElementById('empHourlyRate').value),
          overtimeRateMultiplier: Number(document.getElementById('empOtMultiplier').value),
          standardDailyHours: Number(document.getElementById('empStdHours').value)
        };

    const { ok, data } = await this.api(isEdit ? `Employee/${id}` : 'Employee', {
      method: isEdit ? 'PUT' : 'POST',
      body: JSON.stringify(payload)
    });

    if (!ok || !data.success) {
      this.toast(data.message || 'Lưu thông tin thất bại.', 'error');
      return;
    }

    this.toast(data.message, 'success');
    this.closeModal('employeeModal');
    await this.loadCommonMetadata();
    this.loadEmployees();
  },

  async toggleUserStatus(id, isActive, fullName) {
    const actionText = isActive ? 'mở khóa' : 'đóng (khóa)';
    const ok = await this.showConfirm({
      title: isActive ? 'Mở Khóa Tài Khoản' : 'Khóa Tài Khoản',
      message: `Bạn có chắc chắn muốn <strong>${actionText}</strong> tài khoản của nhân viên <strong>${this.escape(fullName)}</strong>?`,
      confirmText: isActive ? 'Mở Khóa' : 'Khóa Tài Khoản',
      cancelText: 'Hủy Bỏ',
      type: isActive ? 'info' : 'warning',
      icon: isActive ? 'ri-lock-unlock-line' : 'ri-lock-line'
    });
    if (!ok) return;

    const { ok: apiOk, data } = await this.api(`Employee/${id}/status`, {
      method: 'PATCH',
      body: JSON.stringify({ isActive })
    });

    if (apiOk && data.success) {
      this.toast(data.message, 'success');
      this.loadEmployees();
    } else {
      this.toast(data?.message || 'Không thể thay đổi trạng thái.', 'error');
    }
  },

  async resetBiometric(id, fullName) {
    const ok = await this.showConfirm({
      title: 'Hủy Liên Kết Thiết Bị Vân Tay',
      message: `Xác nhận hủy liên kết thiết bị vân tay cũ của <strong>${this.escape(fullName)}</strong> để nhân viên đăng ký lại trên điện thoại mới?`,
      confirmText: 'Xác Nhận Hủy',
      cancelText: 'Giữ Lại',
      type: 'biometric',
      icon: 'ri-fingerprint-line'
    });
    if (!ok) return;

    const { ok: apiOk, data } = await this.api(`Authenticate/reset-biometric/${id}`, { method: 'POST' });
    if (apiOk && data.success) {
      this.toast(data.message, 'success');
      this.loadEmployees();
      if (this.currentTab === 'dashboard') this.loadDashboard();
    } else {
      this.toast(data?.message || 'Reset vân tay thất bại.', 'error');
    }
  },

  async deleteEmployee(id, fullName) {
    const ok = await this.showConfirm({
      title: 'Xóa Vĩnh Viễn Tài Khoản',
      message: `CẢNH BÁO: Xóa vĩnh viễn tài khoản của <strong>${this.escape(fullName)}</strong> và toàn bộ dữ liệu chấm công liên quan?<br><small style="color:var(--text-muted); display:inline-block; margin-top:6px;">Hành động này không thể hoàn tác.</small>`,
      confirmText: 'Xóa Vĩnh Viễn',
      cancelText: 'Hủy Bỏ',
      type: 'danger',
      icon: 'ri-delete-bin-line'
    });
    if (!ok) return;

    const { ok: apiOk, data } = await this.api(`Employee/${id}`, { method: 'DELETE' });
    if (apiOk && data.success) {
      this.toast(data.message, 'success');
      await this.loadCommonMetadata();
      this.loadEmployees();
    } else {
      this.toast(data?.message || 'Xóa thất bại.', 'error');
    }
  },

  // ==================== TAB 3: ATTENDANCE & MANUAL ENTRY ====================
  async loadAttendance(options = {}) {
    const from = document.getElementById('attFromDate')?.value || '';
    const to = document.getElementById('attToDate')?.value || '';
    const dept = document.getElementById('attDeptFilter')?.value || '';
    const userId = document.getElementById('attUserFilter')?.value || '';
    const status = document.getElementById('attStatusFilter')?.value || '';
    const search = document.getElementById('attSearch')?.value.trim() || '';

    const params = new URLSearchParams();
    if (from) params.set('fromDate', from);
    if (to) params.set('toDate', to);
    if (dept) params.set('department', dept);
    if (userId) params.set('userId', userId);
    if (status) params.set('status', status);
    if (search) params.set('search', search);

    const { ok, data } = await this.api(`Attendance/all?${params.toString()}`);
    if (!ok || !data.data) return;

    this.attendanceCache = data.data;
    const isAdmin = this.currentUser?.role === 'Admin';
    const tbody = document.getElementById('attendanceTableBody');
    if (data.data.length === 0) {
      tbody.innerHTML = '<tr><td colspan="9" style="text-align:center; padding:28px; color:var(--text-muted);">Không có bản ghi chấm công nào khớp với bộ lọc.</td></tr>';
      return;
    }

    tbody.innerHTML = data.data.map(r => {
      // Phân định rõ ràng hình thức từng chiều Vào ca và Tan ca:
      // 1. Hybrid: Sáng vào tự chấm Vân tay App (chính chủ có selfie), Chiều về Quản lý chấm bù do quên
      // 2. Pure Manual: Quản lý tạo thủ công 100% (cả vào và ra)
      // 3. Pure Biometric: Cả vào và ra đều tự chấm bằng Vân tay chính chủ qua App
      let methodBadge = '';
      const isHybrid = r.entryType === 'Hybrid' || (r.isManualEntry && r.proofImagePath);
      const isPureManual = r.isManualEntry && !r.proofImagePath;

      if (isHybrid) {
        methodBadge = `
          <div style="display:flex; flex-direction:column; gap:4px; align-items:flex-start;">
            <span class="badge badge-success" style="font-size:10.5px; padding:2px 7px; display:inline-flex; align-items:center; gap:4px;" title="Vào ca tự bấm bằng cảm biến vân tay & selfie khuôn mặt trên app">
              <i class="ri-fingerprint-line"></i> Vào: Vân tay App
            </span>
            <span class="badge badge-warning" style="font-size:10.5px; padding:2px 7px; display:inline-flex; align-items:center; gap:4px;" title="Tan ca được ${this.escape(r.manualEntryByName || 'Quản lý')} đối soát và chấm bù qua Web">
              <i class="ri-edit-2-line"></i> Ra: Bù Quản lý
            </span>
          </div>
        `;
      } else if (isPureManual) {
        methodBadge = `
          <div style="display:flex; flex-direction:column; gap:2px; align-items:flex-start;">
            <span class="badge badge-warning" style="font-size:11px; padding:3px 8px; display:inline-flex; align-items:center; gap:4px;" title="Quản lý tạo mới cả vào và ra">
              <i class="ri-edit-box-line"></i> Thủ công 100%
            </span>
            <span style="font-size:10.5px; color:var(--text-muted); margin-left:2px;">${this.escape(r.manualEntryByName || 'Quản lý tạo')}</span>
          </div>
        `;
      } else {
        methodBadge = `
          <div style="display:flex; flex-direction:column; gap:2px; align-items:flex-start;">
            <span class="badge badge-success" style="font-size:11px; padding:3px 8px; display:inline-flex; align-items:center; gap:4px;" title="Tự động điểm danh vào/ra bằng vân tay chính chủ">
              <i class="ri-fingerprint-line"></i> Vân tay chính chủ
            </span>
            <span style="font-size:10.5px; color:var(--success); font-weight:600; margin-left:2px;">Tự động qua App 100%</span>
          </div>
        `;
      }

      let statusBadge;
      let statusDetail = '';
      if (r.status === 'Completed') {
        statusBadge = '<span class="badge badge-success"><i class="ri-checkbox-circle-line"></i> Hoàn thành</span>';
        statusDetail = '<div style="font-size:11px; color:var(--success); margin-top:2px;">Đã tan ca</div>';
      } else if (r.status === 'MissingCheckout') {
        statusBadge = '<span class="badge badge-danger"><i class="ri-alarm-warning-line"></i> Quên Check-out</span>';
        statusDetail = '<div style="font-size:11px; color:#ef4444; font-weight:600; margin-top:2px;"><i class="ri-map-pin-user-line"></i> Đã quá giờ tan ca • Đã rời văn phòng</div>';
      } else if (r.status === 'ShiftEnded') {
        statusBadge = '<span class="badge badge-warning"><i class="ri-time-line"></i> Hết giờ ca</span>';
        statusDetail = '<div style="font-size:11px; color:#f59e0b; margin-top:2px;">Chờ bấm tan ca hoặc chuẩn bị ra về</div>';
      } else {
        statusBadge = '<span class="badge badge-primary"><i class="ri-user-follow-line"></i> Đang làm việc</span>';
        statusDetail = '<div style="font-size:11px; color:var(--primary); margin-top:2px;"><i class="ri-map-pin-range-line"></i> Trong khuôn viên trụ sở</div>';
      }

      const dateIso = r.attendanceDate ? r.attendanceDate.split('T')[0] : '';
      const myId = (this.currentUser?.userId || this.currentUser?.id || '').toLowerCase();
      const myUsername = (this.currentUser?.username || '').toLowerCase();
      const rowUserId = (r.userId || '').toLowerCase();
      const rowUsername = (r.username || '').toLowerCase();
      const isSelf = (rowUserId && myId && rowUserId === myId) || (rowUsername && myUsername && rowUsername === myUsername);

      return `
        <tr>
          <td><strong>${this.formatDate(r.attendanceDate)}</strong></td>
          <td>
            <div style="display:flex; align-items:center; gap:8px;">
              ${r.proofImagePath ? `
                <div style="position:relative; cursor:pointer;" onclick="App.openProofModal('${r.proofImagePath}', '${this.escape(r.employeeName)}', '${this.formatTime(r.checkInTime)}', '${this.escape(r.shiftName || '')}')" title="Bấm để phóng to xem ảnh Selfie gốc">
                  <img src="${r.proofImagePath}" style="width:36px; height:36px; border-radius:10px; object-fit:cover; border:2px solid var(--primary); display:block; box-shadow:0 2px 8px rgba(0,0,0,0.15);" />
                  <span style="position:absolute; bottom:-3px; right:-3px; background:var(--primary); color:#fff; border-radius:50%; width:15px; height:15px; font-size:9.5px; display:flex; align-items:center; justify-content:center; box-shadow:0 1px 3px rgba(0,0,0,0.3);"><i class="ri-zoom-in-line"></i></span>
                </div>
              ` : ''}
              <div>
                <strong>${this.escape(r.employeeName)}</strong>
                <div style="font-size:11.5px; color:var(--text-muted);">@${this.escape(r.username || '')}</div>
              </div>
            </div>
          </td>
          <td>
            ${this.escape(r.department || 'Chưa phân ban')}
            <div style="font-size:11px; color:var(--primary); font-weight:600; margin-top:2px;">
              <i class="ri-time-line"></i> ${this.escape(r.shiftName || 'Ca Hành Chính (08:00 - 17:00)')}
            </div>
          </td>
          <td><strong style="color:var(--success)">${r.checkInTime ? this.formatTime(r.checkInTime) : '--:--'}</strong></td>
          <td><strong style="color:var(--primary)">${r.checkOutTime ? this.formatTime(r.checkOutTime) : 'Chưa ra'}</strong></td>
          <td><strong>${r.totalWorkingHours}h</strong></td>
          <td>${methodBadge}</td>
          <td>
            ${statusBadge}
            ${statusDetail}
            ${r.remarks ? `<div style="font-size:11px; color:var(--text-muted); margin-top:3px; max-width:280px; line-height:1.4;">${this.escape(r.remarks)}</div>` : ''}
          </td>
          <td style="text-align:right;">
            <div style="display:inline-flex; gap:6px;">
              ${!isAdmin && isSelf ? `
                <span class="badge badge-purple" style="font-size:11px; padding:4px 7px;" title="Để đảm bảo kiểm soát chéo, chỉ Admin mới được sửa công của Quản lý">
                  <i class="ri-shield-user-line"></i> Chỉ Admin sửa
                </span>
              ` : `
                <button class="btn btn-outline btn-sm" onclick="App.openEditAttendanceModal(${r.id})" title="Chấm công bù / Điều chỉnh thông tin ca này">
                  <i class="ri-edit-line"></i> Chấm bù
                </button>
                <button class="btn btn-danger btn-sm" onclick="App.deleteAttendance(${r.id})" title="Xóa bản ghi">
                  <i class="ri-delete-bin-line"></i>
                </button>
              `}
            </div>
          </td>
        </tr>
      `;
    }).join('');
  },

  openProofModal(url, employeeName, checkInTime, shiftName) {
    const imgEl = document.getElementById('modalProofImg');
    if (imgEl) imgEl.src = url;
    const linkEl = document.getElementById('modalProofLink');
    if (linkEl) linkEl.href = url;
    const detEl = document.getElementById('modalProofDetails');
    if (detEl) {
      detEl.innerHTML = `
        <div style="font-size:15px; font-weight:700; color:var(--text-main);">${employeeName}</div>
        <div style="color:var(--text-muted); font-size:12.5px; margin-top:4px;">
          Giờ vào ca: <span style="color:var(--success); font-weight:700;">${checkInTime}</span> &bull; ${shiftName}
        </div>
        <div style="display:inline-flex; align-items:center; gap:6px; background:rgba(34,197,94,0.12); color:#22c55e; border-radius:20px; padding:4px 12px; font-size:12px; margin-top:10px; font-weight:600;">
          <i class="ri-shield-check-line"></i> Xác thực khuôn mặt Selfie &amp; Cảm biến vân tay chính chủ
        </div>
      `;
    }
    this.openModal('proofPhotoModal');
  },

  formatHHmm(d) {
    if (!d) return '';
    if (typeof d === 'string' && d.includes('T')) {
      const timePart = d.split('T')[1];
      if (timePart) {
        const parts = timePart.split(':');
        if (parts.length >= 2) return `${parts[0]}:${parts[1]}`;
      }
    }
    const dt = new Date(d);
    if (isNaN(dt.getTime())) {
      const match = String(d).match(/(\d{2}):(\d{2})/);
      return match ? `${match[1]}:${match[2]}` : '';
    }
    const h = String(dt.getHours()).padStart(2, '0');
    const m = String(dt.getMinutes()).padStart(2, '0');
    return `${h}:${m}`;
  },

  getDefaultCheckoutForShift(shiftName) {
    if (!shiftName) return '17:00';
    if (shiftName.includes('06:00') || shiftName.includes('Sáng')) return '12:00';
    if (shiftName.includes('14:00') || shiftName.includes('Chiều')) return '22:00';
    if (shiftName.includes('18:00') || shiftName.includes('Tối')) return '22:00';
    return '17:00';
  },

  openEditAttendanceModal(recordId) {
    const r = (this.attendanceCache || []).find(x => x.id === recordId);
    if (!r) {
      this.openManualModal();
      return;
    }

    const isAdmin = this.currentUser?.role === 'Admin';
    const myId = (this.currentUser?.userId || this.currentUser?.id || '').toLowerCase();
    const myUsername = (this.currentUser?.username || '').toLowerCase();
    const rowUserId = (r.userId || '').toLowerCase();
    const rowUsername = (r.username || '').toLowerCase();
    const isSelf = (rowUserId && myId && rowUserId === myId) || (rowUsername && myUsername && rowUsername === myUsername);

    if (!isAdmin && isSelf) {
      this.toast('Để đảm bảo nguyên tắc kiểm soát chéo, Quản lý không được phép tự sửa công cho chính mình. Vui lòng liên hệ Admin!', 'warning');
      return;
    }

    this.populateEmployeeSelects();

    // 1. Cập nhật tiêu đề modal & icon
    const titleEl = document.getElementById('manModalTitle');
    if (titleEl) {
      titleEl.innerHTML = `<i class="ri-edit-2-line" style="color:var(--primary);"></i> Điều Chỉnh / Bổ Sung Giờ Công — ${this.escape(r.employeeName)}`;
    }

    // 2. Điền thông tin nhân viên
    const userSel = document.getElementById('manUserId');
    if (userSel) {
      userSel.value = r.userId;
    }

    // 3. Ngày chấm công
    const dateIso = r.attendanceDate ? r.attendanceDate.split('T')[0] : '';
    if (dateIso) document.getElementById('manDate').value = dateIso;

    // 4. Ca làm việc
    const shiftSel = document.getElementById('manShiftName');
    if (shiftSel && r.shiftName) {
      shiftSel.value = r.shiftName;
    }

    // 5. Tùy chọn ghi đè
    const overSel = document.getElementById('manOverride');
    if (overSel) overSel.value = 'true';

    // 6. Giờ vào làm (Check-in) — LẤY ĐÚNG GIỜ VÀO THỰC TẾ TRONG BẢN GHI!
    const inTimeStr = r.checkInTime ? this.formatHHmm(r.checkInTime) : '08:00';
    document.getElementById('manCheckIn').value = inTimeStr;

    // 7. Giờ ra về (Check-out) — Nếu đã có thì lấy đúng giờ ra; nếu chưa ra thì gợi ý giờ hết ca chuẩn
    const defaultOut = this.getDefaultCheckoutForShift(r.shiftName);
    const outTimeStr = r.checkOutTime ? this.formatHHmm(r.checkOutTime) : defaultOut;
    document.getElementById('manCheckOut').value = outTimeStr;

    // 8. Lý do chấm công — Tự động gợi ý thông minh
    const reasonInput = document.getElementById('manReason');
    if (reasonInput) {
      if (r.remarks && r.remarks.includes('[Chấm công thủ công')) {
        const oldReason = r.remarks.replace(/^\[Chấm công thủ công.*?\]:\s*/, '');
        reasonInput.value = oldReason;
      } else if (!r.checkOutTime) {
        reasonInput.value = `Bổ sung giờ tan ca theo đối soát camera lúc ${defaultOut}`;
      } else {
        reasonInput.value = 'Điều chỉnh lại giờ công theo đối soát thực tế';
      }
    }

    // 9. Hiển thị banner ngữ cảnh chi tiết
    const banner = document.getElementById('manRecordInfoBanner');
    if (banner) {
      const fullInTime = r.checkInTime ? this.formatTime(r.checkInTime) : '--:--';
      const fullOutTime = r.checkOutTime ? this.formatTime(r.checkOutTime) : 'Chưa có giờ ra';
      const isMissing = !r.checkOutTime;

      banner.innerHTML = `
        <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:6px;">
          <div>
            <strong style="font-size:13.5px; color:var(--text-main);">${this.escape(r.employeeName)}</strong>
            <span style="color:var(--text-muted); font-size:12px; margin-left:6px;">@${this.escape(r.username || '')}</span>
          </div>
          <span class="badge ${isMissing ? 'badge-danger' : 'badge-success'}" style="font-size:11px;">
            ${isMissing ? '⚠️ Quên Check-out' : '✓ Đã có đủ giờ vào / ra'}
          </span>
        </div>
        <div style="font-size:12px; color:var(--text-muted); line-height:1.5;">
          • Giờ vào ghi nhận: <strong style="color:var(--success);">${fullInTime}</strong> &bull; Giờ ra hiện tại: <strong style="${isMissing ? 'color:var(--danger);' : 'color:var(--primary);'}">${fullOutTime}</strong><br/>
          • Ca: <strong style="color:var(--primary);">${this.escape(r.shiftName || 'Ca Hành Chính')}</strong> &bull; Phòng ban: <strong>${this.escape(r.department || 'Chưa phân ban')}</strong>
        </div>
      `;
      banner.classList.remove('hidden');
    }

    this.previewManualHours();
    this.openModal('manualModal');
  },

  onManualShiftChange() {
    const shift = document.getElementById('manShiftName')?.value || '';
    if (shift.includes('06:00')) {
      document.getElementById('manCheckIn').value = '06:00';
      document.getElementById('manCheckOut').value = '12:00';
    } else if (shift.includes('14:00 - 22:00')) {
      document.getElementById('manCheckIn').value = '14:00';
      document.getElementById('manCheckOut').value = '22:00';
    } else if (shift.includes('18:00')) {
      document.getElementById('manCheckIn').value = '18:00';
      document.getElementById('manCheckOut').value = '22:00';
    } else {
      document.getElementById('manCheckIn').value = '08:00';
      document.getElementById('manCheckOut').value = '17:00';
    }
    this.previewManualHours();
  },

  openManualModal(userId = null, dateStr = null) {
    this.populateEmployeeSelects();

    const titleEl = document.getElementById('manModalTitle');
    if (titleEl) {
      titleEl.innerHTML = '<i class="ri-edit-box-line"></i> Quản Lý Chấm Công Thủ Công Cho Nhân Viên';
    }

    const banner = document.getElementById('manRecordInfoBanner');
    if (banner) banner.classList.add('hidden');

    if (userId) {
      document.getElementById('manUserId').value = userId;
      const emp = (this.employeesCache || []).find(x => x.id === userId);
      if (emp && emp.assignedShift && emp.assignedShift !== 'Xoay ca linh hoạt' && document.getElementById('manShiftName')) {
        document.getElementById('manShiftName').value = emp.assignedShift;
        this.onManualShiftChange();
      }
    } else {
      this.onManualShiftChange();
    }

    const todayStr = new Date().toISOString().split('T')[0];
    document.getElementById('manDate').value = dateStr || todayStr;
    document.getElementById('manOverride').value = 'true';
    document.getElementById('manReason').value = '';

    this.previewManualHours();
    this.openModal('manualModal');
  },

  previewManualHours() {
    const shiftName = document.getElementById('manShiftName')?.value || 'Ca Hành Chính (08:00 - 17:00)';
    const inVal = document.getElementById('manCheckIn')?.value || '08:00';
    const outVal = document.getElementById('manCheckOut')?.value || '17:00';
    const [ih, im] = inVal.split(':').map(Number);
    const [oh, om] = outVal.split(':').map(Number);
    let inMins = ih * 60 + im;
    let outMins = oh * 60 + om;
    if (outMins <= inMins) outMins += 24 * 60;

    // Xác định mốc giờ chuẩn của ca làm việc
    let shiftStartMins = 8 * 60; // 08:00
    let shiftEndMins = 17 * 60;  // 17:00
    let isHanhChinh = true;
    let stdHours = 8.0;

    if (shiftName.includes('06:00') || shiftName.includes('Sáng')) {
      shiftStartMins = 6 * 60;
      shiftEndMins = 12 * 60;
      isHanhChinh = false;
      stdHours = 6.0;
    } else if (shiftName.includes('14:00') || shiftName.includes('Chiều')) {
      shiftStartMins = 14 * 60;
      shiftEndMins = 22 * 60;
      isHanhChinh = false;
      stdHours = 8.0;
    } else if (shiftName.includes('18:00') || shiftName.includes('Tối')) {
      shiftStartMins = 18 * 60;
      shiftEndMins = 22 * 60;
      isHanhChinh = false;
      stdHours = 4.0;
    }

    // QUY TẮC CHUẨN DOANH NGHIỆP:
    // 1. Vào sớm trước giờ ca (ví dụ vào 07:24 khi ca 08:00): Tính từ giờ bắt đầu ca (08:00), KHÔNG tính tăng ca vào sớm!
    const effectiveInMins = Math.max(inMins, shiftStartMins);

    // 2. Tan ca: Chốt trần tại giờ kết thúc ca chuẩn đối với ca tiêu chuẩn
    const effectiveOutMins = Math.min(outMins, shiftEndMins);

    let diff = effectiveOutMins - effectiveInMins;
    if (diff < 0) diff = 0;

    // Ca Hành Chính (08:00 - 17:00): Trừ 1 giờ nghỉ trưa cố định 12:00 - 13:00 (720 - 780 phút)
    if (isHanhChinh && diff > 0) {
      const lunchStart = 12 * 60;
      const lunchEnd = 13 * 60;
      const overlapStart = Math.max(effectiveInMins, lunchStart);
      const overlapEnd = Math.min(effectiveOutMins, lunchEnd);
      if (overlapEnd > overlapStart) {
        diff -= (overlapEnd - overlapStart);
      }
    }

    const hours = Math.round((diff / 60) * 100) / 100;
    const earlyInMins = shiftStartMins > inMins ? (shiftStartMins - inMins) : 0;
    const earlyOutMins = shiftEndMins > outMins ? (shiftEndMins - outMins) : 0;
    const lateInMins = inMins > shiftStartMins ? (inMins - shiftStartMins) : 0;

    const el = document.getElementById('manHoursPreview');
    if (el) {
      if (hours >= stdHours) {
        let note = '';
        if (earlyInMins > 0) {
          note = ` (Đến sớm ${earlyInMins}p • Tính công từ đúng ${Math.floor(shiftStartMins/60)}:00 • 0h OT)`;
        } else {
          note = ' (Đủ công, Hoàn thành ca • 0h OT)';
        }
        el.textContent = `Tổng thời gian ca làm việc: ${hours} giờ [${hours}h tiêu chuẩn]${note}`;
        el.style.color = 'var(--success)';
        el.style.background = 'rgba(34, 197, 94, 0.12)';
      } else {
        let note = '';
        if (lateInMins > 0 && earlyOutMins > 0) {
          note = `Đi trễ ${lateInMins}p, Về sớm ${earlyOutMins}p`;
        } else if (lateInMins > 0) {
          note = `Đi trễ ${lateInMins} phút`;
        } else if (earlyOutMins > 0) {
          note = `Về sớm ${earlyOutMins} phút`;
        } else {
          note = `Thiếu ${Math.round((stdHours - hours) * 60)} phút`;
        }
        if (earlyInMins > 0) {
          note += ` • Đến sớm ${earlyInMins}p không tính công`;
        }
        el.textContent = `Tổng thời gian ca làm việc: ${hours} giờ (${hours}h chuẩn • ${note} • 0h OT)`;
        el.style.color = 'var(--warning)';
        el.style.background = 'rgba(245, 158, 11, 0.12)';
      }
    }
  },

  async submitManualAttendance(e) {
    e.preventDefault();
    const userId = document.getElementById('manUserId').value;
    const shiftName = document.getElementById('manShiftName')?.value || 'Ca Hành Chính (08:00 - 17:00)';
    const date = document.getElementById('manDate').value;
    const checkIn = document.getElementById('manCheckIn').value;
    const checkOut = document.getElementById('manCheckOut').value;
    const reason = document.getElementById('manReason').value.trim();
    const overrideExisting = document.getElementById('manOverride').value === 'true';

    const payload = {
      userId,
      shiftName,
      attendanceDate: `${date}T00:00:00`,
      checkInTime: `${date}T${checkIn}:00`,
      checkOutTime: `${date}T${checkOut}:00`,
      reason,
      overrideExisting
    };

    const { ok, data } = await this.api('Attendance/manual', {
      method: 'POST',
      body: JSON.stringify(payload)
    });

    if (ok && data.success) {
      this.toast(data.message, 'success');
      this.closeModal('manualModal');
      this.refreshCurrentTab();
    } else {
      this.toast(data.message || 'Chấm công thủ công thất bại.', 'error');
    }
  },

  async deleteAttendance(id) {
    const r = (this.attendanceCache || []).find(x => x.id === id);
    const isAdmin = this.currentUser?.role === 'Admin';
    if (r && !isAdmin) {
      const myId = (this.currentUser?.userId || this.currentUser?.id || '').toLowerCase();
      const myUsername = (this.currentUser?.username || '').toLowerCase();
      const rowUserId = (r.userId || '').toLowerCase();
      const rowUsername = (r.username || '').toLowerCase();
      if ((rowUserId && myId && rowUserId === myId) || (rowUsername && myUsername && rowUsername === myUsername)) {
        this.toast('Quản lý không được phép tự xóa bản ghi chấm công của chính mình!', 'warning');
        return;
      }
    }

    const ok = await this.showConfirm({
      title: 'Xóa Bản Ghi Chấm Công',
      message: 'Bạn có chắc chắn muốn xóa bản ghi chấm công này? Dữ liệu tính lương sẽ được cập nhật lại tương ứng.',
      confirmText: 'Xóa Bản Ghi',
      cancelText: 'Hủy Bỏ',
      type: 'danger',
      icon: 'ri-delete-bin-2-line'
    });
    if (!ok) return;

    const { ok: apiOk, data } = await this.api(`Attendance/${id}`, { method: 'DELETE' });
    if (apiOk && data.success) {
      this.toast(data.message, 'success');
      this.loadAttendance();
    } else {
      this.toast(data?.message || 'Không thể xóa bản ghi.', 'error');
    }
  },

  // ==================== TAB 4: SALARY / PAYROLL ====================
  setSalaryPeriodType(type) {
    this.salaryPeriodType = type;
    ['Month', 'Week', 'Custom'].forEach(t => {
      document.getElementById(`salMode${t}`)?.classList.toggle('active', t === type);
    });
    document.getElementById('salYearWrap').classList.toggle('hidden', type === 'Custom');
    document.getElementById('salMonthWrap').classList.toggle('hidden', type !== 'Month');
    document.getElementById('salWeekWrap').classList.toggle('hidden', type !== 'Week');
    document.getElementById('salCustomWrap').classList.toggle('hidden', type !== 'Custom');
    this.loadSalaries();
  },

  async loadSalaries() {
    const params = new URLSearchParams({ periodType: this.salaryPeriodType });
    const dept = document.getElementById('salDeptFilter')?.value || '';
    if (dept) params.set('department', dept);

    if (this.salaryPeriodType === 'Month') {
      params.set('year', document.getElementById('salYear').value);
      params.set('month', document.getElementById('salMonth').value);
    } else if (this.salaryPeriodType === 'Week') {
      params.set('year', document.getElementById('salYear').value);
      params.set('weekNumber', document.getElementById('salWeek').value);
    } else {
      params.set('fromDate', document.getElementById('salFromDate').value);
      params.set('toDate', document.getElementById('salToDate').value);
    }

    const { ok, data } = await this.api(`Salary/calculate?${params.toString()}`);
    if (!ok || !data.data) return;

    const s = data.data;
    this.lastPayrollData = s;

    document.getElementById('salarySummaryKpis').innerHTML = `
      <div class="kpi-card">
        <div>
          <div class="kpi-label">Kỳ Tính Lương</div>
          <div class="kpi-value" style="font-size:19px;">${this.escape(s.periodDescription)}</div>
          <div class="kpi-sub">Số lượng: <strong>${s.employeeCount}</strong> nhân viên</div>
        </div>
        <div class="kpi-icon blue"><i class="ri-calendar-todo-line"></i></div>
      </div>
      <div class="kpi-card">
        <div>
          <div class="kpi-label">Tổng Giờ Làm Thực Tế</div>
          <div class="kpi-value">${s.totalHours} giờ</div>
          <div class="kpi-sub">Giờ chuẩn: <strong>${s.totalRegularHours}h</strong> • Tăng ca OT: <strong style="color:var(--warning)">${s.totalOvertimeHours}h</strong></div>
        </div>
        <div class="kpi-icon amber"><i class="ri-timer-flash-line"></i></div>
      </div>
      <div class="kpi-card">
        <div>
          <div class="kpi-label">Tổng Lương Giờ Chuẩn</div>
          <div class="kpi-value">${this.formatVnd(s.totalRegularSalary)}</div>
          <div class="kpi-sub">Lương OT: <strong style="color:var(--warning)">${this.formatVnd(s.totalOvertimeSalary)}</strong> • Thưởng: <strong style="color:var(--success)">${this.formatVnd(s.totalBonusSalary || 0)}</strong></div>
        </div>
        <div class="kpi-icon purple"><i class="ri-coins-line"></i></div>
      </div>
      <div class="kpi-card">
        <div>
          <div class="kpi-label">Tổng Quỹ Lương Thực Chi</div>
          <div class="kpi-value" style="color:var(--success);">${this.formatVnd(s.totalPayroll)}</div>
          <div class="kpi-sub">Đã gồm lương chuẩn, OT &amp; thưởng thêm</div>
        </div>
        <div class="kpi-icon green"><i class="ri-bank-card-line"></i></div>
      </div>
    `;

    const isAdmin = this.currentUser?.role === 'Admin';
    const tbody = document.getElementById('salaryTableBody');
    tbody.innerHTML = (s.items || []).map(item => `
      <tr>
        <td>
          <strong>${this.escape(item.fullName)}</strong>
          <div style="font-size:11.5px; color:var(--text-muted);">${this.escape(item.email)}</div>
        </td>
        <td>${this.escape(item.department || 'Chưa phân ban')}</td>
        <td><strong>${item.workingDaysCount} ngày</strong></td>
        <td>${item.regularHours}h</td>
        <td><strong style="color:var(--warning)">${item.overtimeHours}h</strong></td>
        <td><strong>${item.totalWorkingHours}h</strong></td>
        <td>
          <strong>${this.formatVnd(item.hourlyRate)}/h</strong>
          <div style="font-size:11px; color:var(--text-muted);">OT x${item.overtimeMultiplier}</div>
        </td>
        <td>${this.formatVnd(item.regularSalary)}</td>
        <td style="color:var(--warning); font-weight:700;">${this.formatVnd(item.overtimeSalary)}</td>
        <td>
          ${item.bonusSalary > 0
            ? `<strong style="color:var(--success); font-size:13.5px;">+${this.formatVnd(item.bonusSalary)}</strong>
               <div style="font-size:11px; color:var(--text-muted); max-width:140px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;" title="${this.escape(item.bonusReason || '')}">⭐ ${this.escape(item.bonusReason || 'Thưởng')}</div>`
            : `<span style="color:var(--text-muted); font-size:12px;">0 đ</span>`}
          <div>
            ${isAdmin ? `
              <button class="btn btn-outline btn-xs" style="margin-top:4px; padding:2px 7px; font-size:11px;" onclick="App.openBonusModal('${item.userId}', '${this.escape(item.fullName)}', ${item.bonusSalary || 0}, '${this.escape(item.bonusReason || '')}')">
                <i class="ri-gift-line"></i> Thưởng
              </button>
            ` : ''}
          </div>
        </td>
        <td><strong style="color:var(--success); font-size:14.5px;">${this.formatVnd(item.totalSalary)}</strong></td>
        <td style="text-align:right;">
          <div style="display:inline-flex; gap:6px;">
            <button class="btn btn-outline btn-sm" onclick="App.previewEmailReport('${item.userId}', '${this.salaryPeriodType === 'Week' ? 'Week' : 'Month'}')" title="Xem phiếu lương & báo cáo chi tiết">
              <i class="ri-file-list-3-line"></i> Phiếu lương
            </button>
            <button class="btn btn-primary btn-sm" onclick="App.sendSingleEmailReport('${item.userId}', '${this.escape(item.fullName)}', '${this.salaryPeriodType === 'Week' ? 'Week' : 'Month'}')" title="Gửi qua Email">
              <i class="ri-mail-send-line"></i> Gửi Mail
            </button>
          </div>
        </td>
      </tr>
    `).join('');
  },

  exportPayrollCsv() {
    if (!this.lastPayrollData || !this.lastPayrollData.items || this.lastPayrollData.items.length === 0) {
      this.toast('Chưa có dữ liệu bảng lương để xuất.', 'warning');
      return;
    }

    const period = this.lastPayrollData.periodDescription || 'Tháng Hiện Tại';
    const cleanPeriod = period.replace(/[\s\/\\]+/g, '_');
    const dateStr = new Date().toISOString().slice(0, 10);
    const fileName = `Bang_Luong_${cleanPeriod}_${dateStr}.xlsx`;

    // Ưu tiên 1: Xuất file Microsoft Excel .xlsx chuẩn xịn 100% bằng SheetJS
    if (window.XLSX) {
      try {
        const title1 = ['HỆ THỐNG ĐIỂM DANH & QUẢN LÝ NHÂN VIÊN DOANH NGHIỆP'];
        const title2 = ['BẢNG TỔNG HỢP THANH TOÁN LƯƠNG NHÂN VIÊN'];
        const title3 = [`Kỳ lương: ${period} | Ngày xuất báo cáo: ${new Date().toLocaleDateString('vi-VN')} | Đơn vị tính: VNĐ`];
        const emptyRow = [];
        const headers = [
          'STT', 'Họ và Tên', 'Tài Khoản', 'Email', 'Phòng Ban', 'Kỳ Lương',
          'Số Ngày Làm', 'Giờ Chuẩn (h)', 'Giờ Tăng Ca (h)', 'Tổng Giờ Làm (h)',
          'Đơn Giá Giờ (VNĐ)', 'Hệ Số OT', 'Lương Chuẩn (VNĐ)', 'Lương Tăng Ca (VNĐ)',
          'Thưởng Thêm (VNĐ)', 'Lý Do Thưởng', 'TỔNG THỰC LĨNH (VNĐ)'
        ];

        let totalDays = 0, totalRegH = 0, totalOtH = 0, totalWorkH = 0;
        let totalRegSal = 0, totalOtSal = 0, totalBonus = 0, totalNet = 0;

        const dataRows = this.lastPayrollData.items.map((i, idx) => {
          const days = i.workingDaysCount || 0;
          const regH = Number((i.regularHours || 0).toFixed(2));
          const otH = Number((i.overtimeHours || 0).toFixed(2));
          const totH = Number((i.totalWorkingHours || 0).toFixed(2));
          const rate = Math.round(i.hourlyRate || 0);
          const mult = Number((i.overtimeMultiplier || 1.5).toFixed(1));
          const regSal = Math.round(i.regularSalary || 0);
          const otSal = Math.round(i.overtimeSalary || 0);
          const bonus = Math.round(i.bonusSalary || 0);
          const total = Math.round(i.totalSalary || 0);

          totalDays += days;
          totalRegH += regH;
          totalOtH += otH;
          totalWorkH += totH;
          totalRegSal += regSal;
          totalOtSal += otSal;
          totalBonus += bonus;
          totalNet += total;

          return [
            idx + 1,
            i.fullName || '',
            i.username || '',
            i.email || '',
            i.department || '',
            i.periodDescription || period,
            days,
            regH,
            otH,
            totH,
            rate,
            mult,
            regSal,
            otSal,
            bonus,
            i.bonusReason || '',
            total
          ];
        });

        const summaryRow = [
          '',
          `TỔNG CỘNG (${this.lastPayrollData.items.length} NHÂN VIÊN)`,
          '', '', '', '',
          totalDays,
          Number(totalRegH.toFixed(2)),
          Number(totalOtH.toFixed(2)),
          Number(totalWorkH.toFixed(2)),
          '', '',
          totalRegSal,
          totalOtSal,
          totalBonus,
          '',
          totalNet
        ];

        const aoa = [
          title1,
          title2,
          title3,
          emptyRow,
          headers,
          ...dataRows,
          summaryRow
        ];

        const ws = XLSX.utils.aoa_to_sheet(aoa);

        // Hợp nhất ô cho tiêu đề bảng và dòng tổng cộng
        ws['!merges'] = [
          { s: { r: 0, c: 0 }, e: { r: 0, c: 16 } },
          { s: { r: 1, c: 0 }, e: { r: 1, c: 16 } },
          { s: { r: 2, c: 0 }, e: { r: 2, c: 16 } },
          { s: { r: aoa.length - 1, c: 1 }, e: { r: aoa.length - 1, c: 5 } }
        ];

        // Độ rộng cột tự động căn chỉnh hoàn hảo từng cột
        ws['!cols'] = [
          { wch: 6 },  // STT
          { wch: 22 }, // Họ tên
          { wch: 15 }, // Username
          { wch: 25 }, // Email
          { wch: 18 }, // Phòng ban
          { wch: 15 }, // Kỳ lương
          { wch: 13 }, // Số ngày làm
          { wch: 13 }, // Giờ chuẩn
          { wch: 14 }, // Giờ OT
          { wch: 15 }, // Tổng giờ
          { wch: 18 }, // Đơn giá giờ
          { wch: 10 }, // Hệ số OT
          { wch: 18 }, // Lương chuẩn
          { wch: 18 }, // Lương OT
          { wch: 16 }, // Thưởng
          { wch: 25 }, // Lý do thưởng
          { wch: 22 }  // Tổng thực lĩnh
        ];

        const wb = XLSX.utils.book_new();
        XLSX.utils.book_append_sheet(wb, ws, 'Bang_Luong');
        XLSX.writeFile(wb, fileName);
        this.toast(`Đã xuất bảng lương Excel (.xlsx) chuẩn sắc nét!`, 'success');
        return;
      } catch (err) {
        console.error('Lỗi khi xuất bằng SheetJS:', err);
      }
    }

    // Dự phòng: Xuất file CSV có 'sep=,' để Excel trên mọi máy tính tự chia cột chuẩn xác
    const headers = ['STT', 'Họ và tên', 'Username', 'Email', 'Phòng ban', 'Kỳ lương', 'Số ngày làm', 'Giờ chuẩn', 'Giờ OT', 'Tổng giờ', 'Đơn giá giờ (VNĐ)', 'Hệ số OT', 'Lương chuẩn (VNĐ)', 'Lương OT (VNĐ)', 'Thưởng thêm (VNĐ)', 'Lý do thưởng', 'Tổng thực lĩnh (VNĐ)'];
    const rows = this.lastPayrollData.items.map((i, idx) => [
      idx + 1, `"${i.fullName}"`, `"${i.username || ''}"`, `"${i.email}"`, `"${i.department || ''}"`, `"${i.periodDescription}"`,
      i.workingDaysCount, i.regularHours, i.overtimeHours, i.totalWorkingHours, i.hourlyRate, i.overtimeMultiplier, i.regularSalary, i.overtimeSalary, i.bonusSalary || 0, `"${i.bonusReason || ''}"`, i.totalSalary
    ]);
    const csvContent = '\uFEFFsep=,\r\n' + [headers.join(','), ...rows.map(r => r.join(','))].join('\r\n');
    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `Bang_Luong_${cleanPeriod}_${dateStr}.csv`;
    a.click();
    URL.revokeObjectURL(url);
    this.toast('Đã xuất bảng lương định dạng Excel.', 'success');
  },

  // ==================== TAB 5: REPORTS & EMAIL ====================
  setReportPeriodType(type) {
    this.reportPeriodType = type;
    ['Week', 'Month', 'Quarter', 'Year'].forEach(t => {
      document.getElementById(`repMode${t}`)?.classList.toggle('active', t === type);
    });
    document.getElementById('repMonthWrap').classList.toggle('hidden', type !== 'Month');
    document.getElementById('repQuarterWrap').classList.toggle('hidden', type !== 'Quarter');
    document.getElementById('repWeekWrap').classList.toggle('hidden', type !== 'Week');
    this.loadReports();
  },

  async loadReports() {
    const params = new URLSearchParams({
      periodType: this.reportPeriodType,
      year: document.getElementById('repYear').value
    });

    if (this.reportPeriodType === 'Month') params.set('month', document.getElementById('repMonth').value);
    if (this.reportPeriodType === 'Quarter') params.set('quarter', document.getElementById('repQuarter').value);
    if (this.reportPeriodType === 'Week') params.set('weekNumber', document.getElementById('repWeek').value);

    const dept = document.getElementById('repDeptFilter')?.value || '';
    if (dept) params.set('department', dept);

    const [repRes, emailStatusRes] = await Promise.all([
      this.api(`Report/overview?${params.toString()}`),
      this.api('Report/email-status')
    ]);

    if (emailStatusRes.ok && emailStatusRes.data?.data) {
      const st = emailStatusRes.data.data;
      const badge = document.getElementById('emailModeBadge');
      if (badge) {
        badge.className = st.isConfigured ? 'badge badge-success' : 'badge badge-primary';
        badge.innerHTML = st.isConfigured
          ? `<i class="ri-mail-check-line"></i> Chế độ: SMTP Gmail Thật (${this.escape(st.senderEmail)})`
          : `<i class="ri-sparkling-line"></i> Chế độ: Xuất & Xem Email HTML Trực Tiếp`;
      }
    }

    if (!repRes.ok || !repRes.data?.data) return;
    const r = repRes.data.data;

    document.getElementById('repPeriodBadge').textContent = r.periodLabel;

    document.getElementById('reportKpis').innerHTML = `
      <div class="kpi-card">
        <div>
          <div class="kpi-label">Kỳ Báo Cáo Thống Kê</div>
          <div class="kpi-value" style="font-size:19px;">${this.escape(r.periodLabel)}</div>
          <div class="kpi-sub">${this.formatDate(r.fromDate)} — ${this.formatDate(r.toDate)}</div>
        </div>
        <div class="kpi-icon blue"><i class="ri-calendar-event-line"></i></div>
      </div>
      <div class="kpi-card">
        <div>
          <div class="kpi-label">Tổng Ngày Công Có Mặt</div>
          <div class="kpi-value">${r.totalDaysPresent} buổi</div>
          <div class="kpi-sub"><strong>${r.employeesWithAttendance}/${r.totalEmployees}</strong> nhân sự phát sinh công</div>
        </div>
        <div class="kpi-icon green"><i class="ri-user-follow-line"></i></div>
      </div>
      <div class="kpi-card">
        <div>
          <div class="kpi-label">Tổng Giờ Làm Trong Kỳ</div>
          <div class="kpi-value">${r.totalHours}h</div>
          <div class="kpi-sub">Bình quân: <strong>${r.averageHoursPerEmployee}h/người</strong> • OT: <strong>${r.totalOvertimeHours}h</strong></div>
        </div>
        <div class="kpi-icon amber"><i class="ri-bar-chart-2-line"></i></div>
      </div>
      <div class="kpi-card">
        <div>
          <div class="kpi-label">Tổng Lương Ước Tính Kỳ Này</div>
          <div class="kpi-value" style="color:var(--success);">${this.formatVnd(r.estimatedPayroll)}</div>
          <div class="kpi-sub">Chấm công bù: <strong>${r.totalManualEntries}</strong> lượt</div>
        </div>
        <div class="kpi-icon purple"><i class="ri-wallet-3-line"></i></div>
      </div>
    `;

    this.renderReportTimelineChart(r.timeline || []);
    this.renderDepartmentsChart('chartReportDepartments', r.departments || []);

    const tbody = document.getElementById('reportEmployeesBody');
    tbody.innerHTML = (r.employees || []).map(e => `
      <tr>
        <td><strong>${this.escape(e.fullName)}</strong></td>
        <td><span style="color:var(--primary); font-weight:600;">${this.escape(e.email)}</span></td>
        <td>${this.escape(e.department || 'Chưa phân ban')}</td>
        <td><strong>${e.daysPresent} ngày</strong></td>
        <td>${e.regularHours}h</td>
        <td style="color:var(--warning); font-weight:700;">${e.overtimeHours}h</td>
        <td><strong>${e.totalHours}h</strong></td>
        <td>${e.manualEntries > 0 ? `<span class="badge badge-warning">${e.manualEntries} lượt</span>` : '<span class="badge badge-muted">0</span>'}</td>
        <td><strong style="color:var(--success);">${this.formatVnd(e.estimatedSalary)}</strong></td>
        <td style="text-align:right;">
          <div style="display:inline-flex; gap:6px;">
            <button class="btn btn-outline btn-sm" onclick="App.previewEmailReport('${e.userId}', '${this.reportPeriodType}')">
              <i class="ri-eye-line"></i> Xem Báo Cáo
            </button>
            <button class="btn btn-primary btn-sm" onclick="App.sendSingleEmailReport('${e.userId}', '${this.escape(e.fullName)}', '${this.reportPeriodType}')">
              <i class="ri-mail-send-line"></i> Gửi Email
            </button>
          </div>
        </td>
      </tr>
    `).join('');
  },

  renderReportTimelineChart(timeline) {
    const ctx = document.getElementById('chartReportTimeline');
    if (!ctx) return;
    if (this.charts.reportTimeline) this.charts.reportTimeline.destroy();

    const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
    const textColor = isDark ? '#94a3b8' : '#64748b';
    const gridColor = isDark ? 'rgba(255,255,255,0.06)' : 'rgba(15,23,42,0.06)';

    this.charts.reportTimeline = new Chart(ctx, {
      data: {
        labels: timeline.map(t => t.periodLabel),
        datasets: [
          {
            type: 'bar',
            label: 'Tổng giờ làm (h)',
            data: timeline.map(t => t.totalHours),
            backgroundColor: 'rgba(14, 165, 233, 0.8)',
            borderRadius: 6
          },
          {
            type: 'line',
            label: 'Số lượt điểm danh',
            data: timeline.map(t => t.totalCheckIns),
            borderColor: '#8b5cf6',
            backgroundColor: '#8b5cf6',
            borderWidth: 2.5,
            tension: 0.3
          }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { labels: { color: textColor, font: { weight: '600' } } }
        },
        scales: {
          x: { ticks: { color: textColor }, grid: { display: false } },
          y: { ticks: { color: textColor }, grid: { color: gridColor } }
        }
      }
    });
  },

  buildEmailPayload(userId, periodType) {
    return {
      userId,
      periodType: periodType || this.reportPeriodType,
      year: Number(document.getElementById('repYear')?.value || new Date().getFullYear()),
      month: Number(document.getElementById('repMonth')?.value || (new Date().getMonth() + 1)),
      quarter: Number(document.getElementById('repQuarter')?.value || 1),
      weekNumber: Number(document.getElementById('repWeek')?.value || this.getIsoWeek(new Date()))
    };
  },

  async previewEmailReport(userId, periodType) {
    const payload = this.buildEmailPayload(userId, periodType);
    const { ok, data } = await this.api('Report/preview-email', {
      method: 'POST',
      body: JSON.stringify(payload)
    });

    if (!ok || !data.success) {
      this.toast(data.message || 'Không thể tải bản xem trước.', 'error');
      return;
    }

    const iframe = document.getElementById('previewIframe');
    iframe.srcdoc = data.data;
    document.getElementById('previewModal').classList.remove('hidden');
  },

  printPreviewFrame() {
    const iframe = document.getElementById('previewIframe');
    if (iframe && iframe.contentWindow) {
      iframe.contentWindow.focus();
      iframe.contentWindow.print();
    }
  },

  async sendSingleEmailReport(userId, fullName, periodType) {
    const payload = this.buildEmailPayload(userId, periodType);
    this.toast(`Đang xử lý gửi báo cáo cho ${fullName}...`, 'info');

    const { ok, data } = await this.api('Report/send-email', {
      method: 'POST',
      body: JSON.stringify(payload)
    });

    if (ok && data.success) {
      this.toast(data.message, 'success');
      if (data.data?.simulated) {
        this.previewEmailReport(userId, periodType);
      }
    } else {
      this.toast(data.message || 'Gửi email thất bại.', 'error');
    }
  },

  async handleSendBulkEmail() {
    const ok = await this.showConfirm({
      title: 'Gửi Email Báo Cáo',
      message: 'Xác nhận gửi Email báo cáo thống kê kỳ này cho <strong>toàn bộ nhân viên</strong> đang hoạt động trong hệ thống?',
      confirmText: 'Bắt Đầu Gửi',
      cancelText: 'Hủy Bỏ',
      type: 'info',
      icon: 'ri-mail-send-line'
    });
    if (!ok) return;

    const payload = {
      periodType: this.reportPeriodType,
      year: Number(document.getElementById('repYear').value),
      month: Number(document.getElementById('repMonth').value),
      quarter: Number(document.getElementById('repQuarter').value),
      weekNumber: Number(document.getElementById('repWeek').value),
      department: document.getElementById('repDeptFilter').value || null
    };

    this.toast('Đang gửi báo cáo hàng loạt...', 'info');
    const { ok: apiOk, data } = await this.api('Report/send-bulk-email', {
      method: 'POST',
      body: JSON.stringify(payload)
    });

    if (apiOk && data.success) {
      this.toast(data.message, 'success');
    } else {
      this.toast(data.message || 'Gửi email hàng loạt thất bại.', 'error');
    }
  },

  async openEmailSettingsModal() {
    try {
      const local = localStorage.getItem('biowork_smtp_config');
      if (local) {
        const saved = JSON.parse(local);
        if (saved.smtpServer) document.getElementById('smtpServer').value = saved.smtpServer;
        if (saved.smtpPort) document.getElementById('smtpPort').value = saved.smtpPort;
        if (saved.senderName) document.getElementById('smtpSenderName').value = saved.senderName;
        if (saved.username) document.getElementById('smtpUsername').value = saved.username;
        if (saved.password) document.getElementById('smtpPassword').value = saved.password;
      } else {
        document.getElementById('smtpUsername').value = 'fup3569@gmail.com';
        document.getElementById('smtpPassword').value = 'lfok lcvv rpur hklk';
      }

      const { ok, data } = await this.api('Report/email-status');
      if (ok && data && data.success && data.data) {
        const s = data.data;
        if (s.smtpServer) document.getElementById('smtpServer').value = s.smtpServer;
        if (s.smtpPort) document.getElementById('smtpPort').value = s.smtpPort;
        if (s.senderName) document.getElementById('smtpSenderName').value = s.senderName;
        const targetEmail = s.senderEmail || s.username || 'fup3569@gmail.com';
        if (targetEmail) document.getElementById('smtpUsername').value = targetEmail;
        if (!document.getElementById('smtpPassword').value) {
          document.getElementById('smtpPassword').value = 'lfok lcvv rpur hklk';
        }
      }
    } catch (_) {}
    document.getElementById('emailSettingsModal').classList.remove('hidden');
  },

  async submitEmailSettings(e) {
    e.preventDefault();
    const payload = {
      smtpServer: document.getElementById('smtpServer').value.trim() || 'smtp.gmail.com',
      smtpPort: Number(document.getElementById('smtpPort').value || 587),
      senderName: document.getElementById('smtpSenderName').value.trim() || 'Hệ thống Nhân sự & Chấm công',
      senderEmail: document.getElementById('smtpUsername').value.trim() || 'fup3569@gmail.com',
      username: document.getElementById('smtpUsername').value.trim() || 'fup3569@gmail.com',
      password: document.getElementById('smtpPassword').value.trim() || 'lfok lcvv rpur hklk',
      enableSsl: true
    };

    try {
      localStorage.setItem('biowork_smtp_config', JSON.stringify(payload));
    } catch (_) {}

    const { ok, data } = await this.api('Report/email-settings', {
      method: 'POST',
      body: JSON.stringify(payload)
    });

    if (ok && data.success) {
      this.toast(data.message, 'success');
      this.closeModal('emailSettingsModal');
      this.loadReports();
    } else {
      this.toast(data.message || 'Không thể lưu cấu hình Email.', 'error');
    }
  },

  async loadPositionRates() {
    try {
      const { ok, data } = await this.api('Salary/position-rates');
      if (ok && data.success && Array.isArray(data.data)) {
        this.positionRatesCache = data.data;
        this.populatePositionPresetDropdown();
        this.renderPositionRatesTable();
      }
    } catch (_) {}
  },

  populatePositionPresetDropdown() {
    const sel = document.getElementById('empPositionPreset');
    if (!sel || !this.positionRatesCache.length) return;
    const currentVal = sel.value;
    sel.innerHTML = this.positionRatesCache.map(p => `
      <option value="${this.escape(p.positionName)}|${p.hourlyRate}|${p.role}">${this.escape(p.positionName)} — ${this.formatVnd(p.hourlyRate)}/giờ</option>
    `).join('') + '<option value="custom">Tự nhập chức vụ riêng (Không dùng mẫu)...</option>';
    if (currentVal) sel.value = currentVal;
  },

  renderPositionRatesTable() {
    const tbody = document.getElementById('positionRatesTableBody');
    if (!tbody || !this.positionRatesCache.length) return;
    const isAdmin = this.currentUser && this.currentUser.role === 'Admin';
    tbody.innerHTML = this.positionRatesCache.map((p, idx) => `
      <tr>
        <td><strong>${this.escape(p.positionName)}</strong></td>
        <td>
          <div style="display:flex; align-items:center; gap:4px;">
            <input type="number" id="posRate_${idx}" class="form-control" style="width:120px; padding:6px 8px; font-weight:700;" value="${p.hourlyRate}" min="1000" step="1000" ${isAdmin ? '' : 'disabled readonly'} />
            <span style="font-size:12px; color:var(--text-muted);">đ/h</span>
          </div>
        </td>
        <td>
          <select id="posRole_${idx}" class="form-control" style="width:105px; padding:6px 8px;" ${isAdmin ? '' : 'disabled'}>
            <option value="User" ${p.role === 'User' ? 'selected' : ''}>User</option>
            <option value="Manager" ${p.role === 'Manager' ? 'selected' : ''}>Manager</option>
            <option value="Admin" ${p.role === 'Admin' ? 'selected' : ''}>Admin</option>
          </select>
        </td>
        <td style="text-align:right;">
          ${isAdmin ? `
            <button class="btn btn-primary btn-sm" onclick="App.savePositionRate(${idx})">
              <i class="ri-save-line"></i> Lưu &amp; Đồng bộ
            </button>
          ` : `
            <span class="badge badge-purple" style="font-size:11.5px; padding:4px 8px;"><i class="ri-lock-line"></i> Chỉ Admin</span>
          `}
        </td>
      </tr>
    `).join('');
  },

  async openPositionRatesModal() {
    await this.loadPositionRates();
    this.renderPositionRatesTable();
    document.getElementById('positionRatesModal').classList.remove('hidden');
  },

  async savePositionRate(idx) {
    const item = this.positionRatesCache[idx];
    if (!item) return;
    const rateInput = document.getElementById(`posRate_${idx}`);
    const roleInput = document.getElementById(`posRole_${idx}`);
    const rate = Number(rateInput?.value || item.hourlyRate);
    const role = roleInput?.value || item.role;

    this.toast(`Đang cập nhật định mức chức vụ ${item.positionName}...`, 'info');
    const { ok, data } = await this.api('Salary/position-rates', {
      method: 'PUT',
      body: JSON.stringify({
        positionName: item.positionName,
        hourlyRate: rate,
        role: role,
        applyToCurrentEmployees: true
      })
    });

    if (ok && data.success) {
      this.toast(data.message, 'success');
      await this.loadPositionRates();
      await this.loadEmployees();
      if (!document.getElementById('tab-salary').classList.contains('hidden')) {
        await this.loadSalaries();
      }
    } else {
      this.toast(data.message || 'Cập nhật thất bại.', 'error');
    }
  },

  openBonusModal(userId = '', fullName = '', currentAmount = 0, currentReason = '') {
    const userSelect = document.getElementById('bonusUserId');
    if (userSelect) {
      userSelect.innerHTML = (this.employeesCache || []).map(e => `
        <option value="${e.id}" ${e.id === userId ? 'selected' : ''}>${this.escape(e.fullName)} (${this.escape(e.position || e.department || 'Nhân viên')})</option>
      `).join('');
    }

    const yearVal = Number(document.getElementById('salYear')?.value || new Date().getFullYear());
    const monthVal = Number(document.getElementById('salMonth')?.value || (new Date().getMonth() + 1));
    document.getElementById('bonusYear').value = yearVal;
    document.getElementById('bonusMonth').value = monthVal;
    document.getElementById('bonusAmount').value = currentAmount > 0 ? currentAmount : 1000000;
    document.getElementById('bonusReason').value = currentReason || 'Thưởng dự án hoàn thành xuất sắc';

    document.getElementById('bonusModal').classList.remove('hidden');
  },

  async submitBonusForm(e) {
    e.preventDefault();
    const payload = {
      userId: document.getElementById('bonusUserId').value,
      year: Number(document.getElementById('bonusYear').value),
      month: Number(document.getElementById('bonusMonth').value),
      amount: Number(document.getElementById('bonusAmount').value),
      reason: document.getElementById('bonusReason').value.trim()
    };

    this.toast('Đang lưu thông tin tiền thưởng...', 'info');
    const { ok, data } = await this.api('Salary/bonus', {
      method: 'POST',
      body: JSON.stringify(payload)
    });

    if (ok && data.success) {
      this.toast(data.message, 'success');
      this.closeModal('bonusModal');
      await this.loadSalaries();
    } else {
      this.toast(data.message || 'Lưu tiền thưởng thất bại.', 'error');
    }
  },

  openModal(id) {
    const el = document.getElementById(id);
    if (el) {
      el.classList.remove('hidden');
      document.body.style.overflow = 'hidden';
    }
  },

  closeModal(id) {
    const el = document.getElementById(id);
    if (el) {
      el.classList.add('hidden');
      document.body.style.overflow = '';
    }
  }
};

window.addEventListener('DOMContentLoaded', () => App.init());
